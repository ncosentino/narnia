using System.Security.Cryptography;
using System.Text;
using NexusLabs.Narnia.Core.Models;
using NexusLabs.Narnia.Core.Repositories;

namespace NexusLabs.Narnia.Core.Services;

/// <summary>
/// Samples process resources and attributes live Copilot runtimes to sessions and terminals.
/// </summary>
public sealed class ProcessDiagnosticsService(
    IProcessResourceSnapshotProvider snapshotProvider,
    ICopilotSessionLockReader lockReader,
    ISessionRepository sessionRepository,
    TimeProvider timeProvider) : IProcessDiagnosticsService
{
    private static readonly TimeSpan MinimumCaptureInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan SessionMappingRefreshInterval = TimeSpan.FromSeconds(30);
    private static readonly string[] ShellProcessNames =
        ["pwsh.exe", "powershell.exe", "cmd.exe", "bash.exe", "zsh.exe", "wsl.exe"];

    private readonly SemaphoreSlim _sampleGate = new(1, 1);
    private ProcessResourceSnapshot? _previousResourceSnapshot;
    private ProcessDiagnosticsSnapshot? _cachedDiagnostics;
    private long _lastCaptureTimestamp;
    private SessionMappingCache? _sessionMappingCache;

    /// <inheritdoc />
    public async ValueTask<ProcessDiagnosticsSnapshot> GetSnapshotAsync(
        CancellationToken ct = default)
    {
        await _sampleGate.WaitAsync(ct);
        try
        {
            var now = timeProvider.GetTimestamp();
            if (_cachedDiagnostics is not null &&
                _lastCaptureTimestamp != 0 &&
                timeProvider.GetElapsedTime(_lastCaptureTimestamp, now) < MinimumCaptureInterval)
            {
                return _cachedDiagnostics;
            }

            var resourceSnapshot = snapshotProvider.Capture(ct);
            _lastCaptureTimestamp = timeProvider.GetTimestamp();
            if (!snapshotProvider.IsSupported || !resourceSnapshot.IsAvailable)
            {
                _cachedDiagnostics = Unavailable(resourceSnapshot);
                return _cachedDiagnostics;
            }

            var usage = BuildOwnUsage(resourceSnapshot, _previousResourceSnapshot, out var sampleDuration);
            var graph = new ProcessGraph(resourceSnapshot.Processes, usage);
            var copilotProcesses = graph.FindByName("copilot.exe");
            var sessionMappings = await GetSessionMappingsAsync(
                resourceSnapshot,
                copilotProcesses,
                ct);

            var runtimeProcesses = copilotProcesses
                .Where(process =>
                    sessionMappings.GetValueOrDefault(process.ProcessId, []).Count > 0 ||
                    !graph.HasAncestorNamed(process.ProcessId, "copilot.exe"))
                .ToArray();
            var runtimes = runtimeProcesses
                .Select(process => BuildRuntime(graph, process, sessionMappings))
                .ToArray();

            var terminals = BuildTerminals(graph, runtimes);
            var terminalRuntimeIds = terminals
                .SelectMany(terminal => terminal.Runtimes)
                .Select(runtime => runtime.CopilotProcessId)
                .ToHashSet();
            var orphaned = runtimes
                .Where(runtime => !terminalRuntimeIds.Contains(runtime.CopilotProcessId))
                .OrderByDescending(runtime => runtime.RuntimeTree.TreeUsage.CpuPercent ?? -1d)
                .ThenByDescending(runtime => runtime.RuntimeTree.TreeUsage.PrivateBytes)
                .ThenBy(runtime => runtime.CopilotProcessId)
                .ToArray();

            var sampledProcessIds = resourceSnapshot.Processes
                .Where(process => process.ProcessId != 0 &&
                    !string.Equals(process.Name, "System Idle Process", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(process.Name, "Idle", StringComparison.OrdinalIgnoreCase))
                .Select(process => process.ProcessId);
            var copilotRuntimeIds = runtimes
                .SelectMany(runtime => graph.GetSubtreeIds(runtime.CopilotProcessId))
                .Distinct();
            var terminalIds = terminals
                .SelectMany(terminal => graph.GetSubtreeIds(terminal.TerminalProcessId))
                .Distinct();
            var renderedProcessIds = terminals
                .SelectMany(terminal => graph.GetSubtreeIds(terminal.TerminalProcessId))
                .Concat(orphaned.SelectMany(runtime =>
                    graph.GetSubtreeIds(runtime.CopilotProcessId)))
                .Distinct()
                .ToArray();
            var mappedSessionCount = runtimes
                .SelectMany(runtime => runtime.Sessions)
                .Select(session => session.SessionId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
            var processTreeIdentities = BuildProcessTreeIdentities(
                graph,
                renderedProcessIds);

            _cachedDiagnostics = new ProcessDiagnosticsSnapshot(
                true,
                null,
                resourceSnapshot.CapturedAt,
                sampleDuration?.TotalSeconds,
                resourceSnapshot.LogicalProcessorCount,
                BuildTopologySignature(terminals, orphaned),
                Hash(string.Join("|", processTreeIdentities)),
                processTreeIdentities,
                graph.Aggregate(sampledProcessIds),
                graph.Aggregate(copilotRuntimeIds),
                graph.Aggregate(terminalIds),
                mappedSessionCount,
                terminals,
                orphaned);
            _previousResourceSnapshot = resourceSnapshot;
            return _cachedDiagnostics;
        }
        finally
        {
            _sampleGate.Release();
        }
    }

    private static IReadOnlyDictionary<int, ProcessUsage> BuildOwnUsage(
        ProcessResourceSnapshot current,
        ProcessResourceSnapshot? previous,
        out TimeSpan? sampleDuration)
    {
        sampleDuration = previous is null
            ? null
            : current.MonotonicTime - previous.MonotonicTime;
        var previousById = previous?.Processes.ToDictionary(process => process.ProcessId);
        var result = new Dictionary<int, ProcessUsage>(current.Processes.Count);

        foreach (var process in current.Processes)
        {
            double? cpuPercent = null;
            if (sampleDuration > TimeSpan.Zero &&
                previousById is not null &&
                previousById.TryGetValue(process.ProcessId, out var prior) &&
                IsSameProcess(prior, process) &&
                process.TotalProcessorTime >= prior.TotalProcessorTime)
            {
                var processorDelta = process.TotalProcessorTime - prior.TotalProcessorTime;
                var capacity = sampleDuration.Value.TotalSeconds *
                    Math.Max(1, current.LogicalProcessorCount);
                cpuPercent = capacity <= 0
                    ? null
                    : Math.Clamp(processorDelta.TotalSeconds / capacity * 100d, 0d, 100d);
            }

            result[process.ProcessId] = new ProcessUsage(
                cpuPercent,
                cpuPercent is null ? 0 : 1,
                1,
                Math.Max(0, process.PrivateBytes),
                Math.Max(0, process.WorkingSetBytes));
        }

        return result;
    }

    private async ValueTask<IReadOnlyDictionary<int, IReadOnlyList<ProcessSessionReference>>>
        GetSessionMappingsAsync(
            ProcessResourceSnapshot snapshot,
            IReadOnlyCollection<ProcessResourceRecord> copilotProcesses,
            CancellationToken ct)
    {
        var signature = string.Join(
            "|",
            copilotProcesses
                .OrderBy(process => process.ProcessId)
                .Select(process => $"{process.ProcessId}:{process.StartedAt?.UtcTicks ?? 0}"));

        if (_sessionMappingCache is not null &&
            string.Equals(_sessionMappingCache.ProcessSignature, signature, StringComparison.Ordinal) &&
            snapshot.MonotonicTime - _sessionMappingCache.SampledAt < SessionMappingRefreshInterval)
        {
            return _sessionMappingCache.Mappings;
        }

        var processIds = copilotProcesses.Select(process => process.ProcessId).ToArray();
        var idsByProcess = lockReader.GetSessionIdsByProcess(processIds);
        var allSessionIds = idsByProcess.Values
            .SelectMany(ids => ids)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var sessions = await sessionRepository.GetByIdsAsync(allSessionIds, ct);

        var mappings = new Dictionary<int, IReadOnlyList<ProcessSessionReference>>();
        foreach (var processId in processIds)
        {
            var mappedIds = idsByProcess.GetValueOrDefault(processId, []);
            var orderedIds = mappedIds
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(sessionId => sessions.ContainsKey(sessionId) ? 0 : 1)
                .ThenBy(sessionId => sessions.GetValueOrDefault(sessionId)?.CreatedAt)
                .ThenBy(sessionId => sessionId, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var references = new List<ProcessSessionReference>(orderedIds.Length);
            for (var index = 0; index < orderedIds.Length; index++)
            {
                var sessionId = orderedIds[index];
                sessions.TryGetValue(sessionId, out var session);
                references.Add(new ProcessSessionReference(
                    sessionId,
                    session?.Summary,
                    session?.Repository,
                    session?.Branch,
                    session?.Cwd,
                    index == 0));
            }

            mappings[processId] = references;
        }

        _sessionMappingCache = new SessionMappingCache(signature, snapshot.MonotonicTime, mappings);
        return mappings;
    }

    private static CopilotRuntimeDiagnostics BuildRuntime(
        ProcessGraph graph,
        ProcessResourceRecord process,
        IReadOnlyDictionary<int, IReadOnlyList<ProcessSessionReference>> sessionMappings)
    {
        var terminalProcessId = graph.FindOwningTerminal(process.ProcessId);
        var launchChain = graph.GetLaunchChain(process.ProcessId, terminalProcessId);
        var shellProcessId = launchChain
            .FirstOrDefault(descriptor => ShellProcessNames.Contains(
                descriptor.Name,
                StringComparer.OrdinalIgnoreCase))
            ?.ProcessId;

        return new CopilotRuntimeDiagnostics(
            process.ProcessId,
            shellProcessId,
            terminalProcessId,
            process.StartedAt,
            launchChain,
            graph.BuildTree(process.ProcessId),
            sessionMappings.GetValueOrDefault(process.ProcessId, []));
    }

    private static IReadOnlyList<TerminalProcessDiagnostics> BuildTerminals(
        ProcessGraph graph,
        IReadOnlyCollection<CopilotRuntimeDiagnostics> runtimes)
    {
        var runtimesByTerminal = runtimes
            .Where(runtime => runtime.TerminalProcessId is not null)
            .GroupBy(runtime => runtime.TerminalProcessId!.Value)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var terminals = new List<TerminalProcessDiagnostics>();

        foreach (var process in graph.FindByName("WindowsTerminal.exe"))
        {
            var terminalRuntimes = runtimesByTerminal.GetValueOrDefault(process.ProcessId, []);
            var terminalIds = graph.GetSubtreeIds(process.ProcessId);
            var runtimeIds = terminalRuntimes
                .SelectMany(runtime => graph.GetSubtreeIds(runtime.CopilotProcessId))
                .ToHashSet();
            var otherIds = terminalIds.Where(processId => !runtimeIds.Contains(processId));

            terminals.Add(new TerminalProcessDiagnostics(
                process.ProcessId,
                process.StartedAt,
                graph.BuildTree(process.ProcessId),
                graph.Aggregate(otherIds),
                terminalRuntimes
                    .OrderByDescending(runtime => runtime.RuntimeTree.TreeUsage.CpuPercent ?? -1d)
                    .ThenByDescending(runtime => runtime.RuntimeTree.TreeUsage.PrivateBytes)
                    .ThenBy(runtime => runtime.CopilotProcessId)
                    .ToArray()));
        }

        terminals.Sort(static (left, right) =>
        {
            var cpu = Nullable.Compare(
                right.ProcessTree.TreeUsage.CpuPercent,
                left.ProcessTree.TreeUsage.CpuPercent);
            if (cpu != 0)
                return cpu;
            var memory = right.ProcessTree.TreeUsage.PrivateBytes.CompareTo(
                left.ProcessTree.TreeUsage.PrivateBytes);
            return memory != 0
                ? memory
                : left.TerminalProcessId.CompareTo(right.TerminalProcessId);
        });
        return terminals;
    }

    private static string BuildTopologySignature(
        IReadOnlyCollection<TerminalProcessDiagnostics> terminals,
        IReadOnlyCollection<CopilotRuntimeDiagnostics> orphanedRuntimes)
    {
        var terminalTokens = terminals
            .OrderBy(terminal => terminal.TerminalProcessId)
            .Select(terminal =>
                $"t:{terminal.TerminalProcessId}:{terminal.StartedAt?.UtcTicks ?? 0}");
        var runtimeTokens = terminals
            .SelectMany(terminal => terminal.Runtimes)
            .Concat(orphanedRuntimes)
            .OrderBy(runtime => runtime.CopilotProcessId)
            .Select(runtime => string.Join(
                ":",
                "c",
                runtime.CopilotProcessId,
                runtime.StartedAt?.UtcTicks ?? 0,
                runtime.TerminalProcessId?.ToString() ?? "orphan",
                runtime.ShellProcessId?.ToString() ?? "no-shell",
                string.Join(",", runtime.LaunchChain.Select(process =>
                    $"{process.ProcessId}-{process.ParentProcessId}-{process.StartedAt?.UtcTicks ?? 0}")),
                string.Join(",", runtime.Sessions.Select(BuildSessionSignature))));
        var payload = string.Join("|", terminalTokens.Concat(runtimeTokens));
        return Hash(payload);
    }

    private static IReadOnlyList<string> BuildProcessTreeIdentities(
        ProcessGraph graph,
        IReadOnlyCollection<int> processIds)
    {
        return processIds
            .Select(graph.GetProcess)
            .Where(process => process is not null)
            .OrderBy(process => process!.ProcessId)
            .Select(process => string.Join(
                ":",
                process!.ProcessId,
                process.ParentProcessId,
                process.StartedAt?.UtcTicks ?? 0))
            .ToArray();
    }

    private static bool IsSameProcess(
        ProcessResourceRecord previous,
        ProcessResourceRecord current)
    {
        return previous.StartedAt is not null &&
            current.StartedAt is not null &&
            previous.StartedAt == current.StartedAt;
    }

    private static ProcessDiagnosticsSnapshot Unavailable(ProcessResourceSnapshot snapshot)
    {
        var empty = new ProcessUsage(null, 0, 0, 0, 0);
        return new ProcessDiagnosticsSnapshot(
            false,
            snapshot.UnavailableReason ?? "Process diagnostics are unavailable.",
            snapshot.CapturedAt,
            null,
            snapshot.LogicalProcessorCount,
            string.Empty,
            string.Empty,
            [],
            empty,
            empty,
            empty,
            0,
            [],
            []);
    }

    private static string Hash(string payload) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));

    private static string BuildSessionSignature(ProcessSessionReference session) =>
        string.Concat(
            SignaturePart(session.SessionId),
            SignaturePart(session.Summary),
            SignaturePart(session.Repository),
            SignaturePart(session.Branch),
            SignaturePart(session.Cwd),
            session.IsPrimary ? "1" : "0");

    private static string SignaturePart(string? value) =>
        value is null ? "-1:" : $"{value.Length}:{value}";

    private sealed record SessionMappingCache(
        string ProcessSignature,
        TimeSpan SampledAt,
        IReadOnlyDictionary<int, IReadOnlyList<ProcessSessionReference>> Mappings);
}
