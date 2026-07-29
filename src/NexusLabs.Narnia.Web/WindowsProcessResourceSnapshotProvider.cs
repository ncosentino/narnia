using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using NexusLabs.Narnia.Core.Models;
using NexusLabs.Narnia.Core.Repositories;

namespace NexusLabs.Narnia.Web;

/// <summary>
/// Captures fast CPU and memory samples through <see cref="Process"/> while periodically
/// refreshing parent-process topology through one read-only WMI query.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsProcessResourceSnapshotProvider(
    ILogger<WindowsProcessResourceSnapshotProvider> logger)
    : IProcessResourceSnapshotProvider
{
    private static readonly TimeSpan ParentRefreshInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan TopologyFailureBackoff = TimeSpan.FromSeconds(10);
    private const string ParentQuery =
        "SELECT ProcessId, ParentProcessId FROM Win32_Process";

    private readonly object _topologyGate = new();
    private IReadOnlyDictionary<int, ParentProcessLink> _parentByProcessId =
        new Dictionary<int, ParentProcessLink>();
    private long _lastParentRefreshTimestamp;
    private long _lastTopologyFailureTimestamp;
    private string? _lastTopologyFailure;
    private string _trackedProcessSignature = string.Empty;

    /// <inheritdoc />
    public bool IsSupported => true;

    /// <inheritdoc />
    public ProcessResourceSnapshot Capture(CancellationToken ct = default)
    {
        Process[] processes;
        try
        {
            processes = Process.GetProcesses();
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
            Win32Exception)
        {
            return Unavailable($"Windows process enumeration failed: {exception.Message}");
        }

        try
        {
            var identities = ReadIdentities(processes, ct);
            var parentResult = GetParentProcesses(identities, ct);
            if (!parentResult.IsAvailable)
                return Unavailable(parentResult.Error!);

            var records = new List<ProcessResourceRecord>(identities.Count);
            foreach (var identity in identities)
            {
                ct.ThrowIfCancellationRequested();
                var parentProcessId = parentResult.Parents.GetValueOrDefault(identity.ProcessId);
                if (TryReadResources(identity, parentProcessId, out var record))
                    records.Add(record);
            }

            return new ProcessResourceSnapshot(
                true,
                null,
                DateTimeOffset.UtcNow,
                Stopwatch.GetElapsedTime(0, Stopwatch.GetTimestamp()),
                Math.Max(1, Environment.ProcessorCount),
                records);
        }
        finally
        {
            foreach (var process in processes)
                process.Dispose();
        }
    }

    private ParentProcessResult GetParentProcesses(
        IReadOnlyCollection<ProcessIdentity> identities,
        CancellationToken ct)
    {
        var trackedSignature = string.Join(
            "|",
            identities
                .Where(identity =>
                    string.Equals(identity.Name, "copilot.exe", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(identity.Name, "WindowsTerminal.exe", StringComparison.OrdinalIgnoreCase))
                .OrderBy(identity => identity.ProcessId)
                .Select(identity =>
                    $"{identity.ProcessId}:{identity.StartedAt.UtcTicks}"));

        lock (_topologyGate)
        {
            var now = Stopwatch.GetTimestamp();
            var shouldRefresh =
                _parentByProcessId.Count == 0 ||
                _lastParentRefreshTimestamp == 0 ||
                Stopwatch.GetElapsedTime(_lastParentRefreshTimestamp, now) >= ParentRefreshInterval ||
                !string.Equals(
                    trackedSignature,
                    _trackedProcessSignature,
                    StringComparison.Ordinal);
            var backingOff = _lastTopologyFailureTimestamp != 0 &&
                Stopwatch.GetElapsedTime(_lastTopologyFailureTimestamp, now) <
                    TopologyFailureBackoff;
            if (backingOff)
            {
                return _parentByProcessId.Count == 0
                    ? new ParentProcessResult(
                        false,
                        _lastTopologyFailure ??
                            "Windows process topology is temporarily unavailable.",
                        new Dictionary<int, int>())
                    : AvailableParents(identities);
            }

            if (!shouldRefresh)
            {
                RefreshMissingParents(identities, ct);
                return AvailableParents(identities);
            }

            try
            {
                _parentByProcessId = BindParents(
                    QueryParents(null, ct),
                    identities);
                _trackedProcessSignature = trackedSignature;
                _lastParentRefreshTimestamp = Stopwatch.GetTimestamp();
                _lastTopologyFailureTimestamp = 0;
                _lastTopologyFailure = null;
                return AvailableParents(identities);
            }
            catch (Exception exception) when (
                exception is ManagementException or
                COMException or
                UnauthorizedAccessException)
            {
                RecordTopologyFailure(exception);
                if (_parentByProcessId.Count > 0)
                {
                    logger.LogWarning(
                        exception,
                        "Could not refresh the full process-parent topology; retaining verified cached links.");
                    RefreshMissingParents(identities, ct);
                    return AvailableParents(identities);
                }
                return new ParentProcessResult(
                    false,
                    $"Windows process topology failed: {exception.Message}",
                    new Dictionary<int, int>());
            }
        }
    }

    private void RefreshMissingParents(
        IReadOnlyCollection<ProcessIdentity> identities,
        CancellationToken ct)
    {
        var missing = identities
            .Where(identity =>
                !_parentByProcessId.TryGetValue(identity.ProcessId, out var link) ||
                link.StartedAt != identity.StartedAt)
            .Select(identity => identity.ProcessId)
            .ToArray();
        if (missing.Length == 0)
            return;

        try
        {
            var updates = BindParents(QueryParents(missing, ct), identities);
            if (updates.Count == 0)
                return;

            var merged = _parentByProcessId.ToDictionary(pair => pair.Key, pair => pair.Value);
            foreach (var (processId, link) in updates)
                merged[processId] = link;
            _parentByProcessId = merged;
            _lastTopologyFailureTimestamp = 0;
            _lastTopologyFailure = null;
        }
        catch (Exception exception) when (
            exception is ManagementException or
            COMException or
            UnauthorizedAccessException)
        {
            RecordTopologyFailure(exception);
            logger.LogWarning(
                exception,
                "Could not resolve parent links for newly observed processes.");
        }
    }

    private void RecordTopologyFailure(Exception exception)
    {
        _lastTopologyFailureTimestamp = Stopwatch.GetTimestamp();
        _lastTopologyFailure = $"Windows process topology failed: {exception.Message}";
    }

    private static IReadOnlyDictionary<int, int> QueryParents(
        IReadOnlyCollection<int>? processIds,
        CancellationToken ct)
    {
        var query = processIds is null
            ? ParentQuery
            : $"{ParentQuery} WHERE {string.Join(
                " OR ",
                processIds.Select(processId => $"ProcessId = {processId}"))}";
        var parents = new Dictionary<int, int>();
        using var searcher = new ManagementObjectSearcher(query);
        using var results = searcher.Get();
        foreach (var item in results)
        {
            ct.ThrowIfCancellationRequested();
            using var process = item;
            if (!TryReadInt32(process["ProcessId"], out var processId))
                continue;
            TryReadInt32(process["ParentProcessId"], out var parentProcessId);
            parents[processId] = parentProcessId;
        }

        return parents;
    }

    private static IReadOnlyDictionary<int, ParentProcessLink> BindParents(
        IReadOnlyDictionary<int, int> parents,
        IReadOnlyCollection<ProcessIdentity> identities)
    {
        var identitiesById = identities.ToDictionary(identity => identity.ProcessId);
        return parents.ToDictionary(
            pair => pair.Key,
            pair => new ParentProcessLink(
                pair.Value,
                identitiesById.GetValueOrDefault(pair.Key)?.StartedAt));
    }

    private ParentProcessResult AvailableParents(
        IReadOnlyCollection<ProcessIdentity> identities)
    {
        var safeParents = new Dictionary<int, int>();
        foreach (var identity in identities)
        {
            if (!_parentByProcessId.TryGetValue(identity.ProcessId, out var link) ||
                link.StartedAt != identity.StartedAt)
            {
                continue;
            }

            safeParents[identity.ProcessId] = link.ParentProcessId;
        }

        return new ParentProcessResult(true, null, safeParents);
    }

    private static IReadOnlyList<ProcessIdentity> ReadIdentities(
        IReadOnlyCollection<Process> processes,
        CancellationToken ct)
    {
        var identities = new List<ProcessIdentity>(processes.Count);
        foreach (var process in processes)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var name = process.ProcessName;
                if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    name += ".exe";
                var startedAt = ReadStartedAt(process);
                if (startedAt is null)
                    continue;
                identities.Add(new ProcessIdentity(process.Id, name, startedAt.Value, process));
            }
            catch (Exception exception) when (
                exception is InvalidOperationException or
                Win32Exception or
                NotSupportedException)
            {
            }
        }

        return identities;
    }

    private static bool TryReadResources(
        ProcessIdentity identity,
        int parentProcessId,
        out ProcessResourceRecord record)
    {
        record = default!;
        try
        {
            record = new ProcessResourceRecord(
                identity.ProcessId,
                parentProcessId,
                identity.Name,
                identity.StartedAt,
                identity.Process.TotalProcessorTime,
                Math.Max(0, identity.Process.WorkingSet64),
                Math.Max(0, identity.Process.PrivateMemorySize64));
            return true;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
            Win32Exception or
            NotSupportedException)
        {
            return false;
        }
    }

    private static DateTimeOffset? ReadStartedAt(Process process)
    {
        try
        {
            return new DateTimeOffset(process.StartTime);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
            Win32Exception or
            NotSupportedException)
        {
            return null;
        }
    }

    private static bool TryReadInt32(object? value, out int result)
    {
        result = 0;
        if (value is null)
            return false;

        try
        {
            var parsed = Convert.ToUInt32(value, CultureInfo.InvariantCulture);
            if (parsed > int.MaxValue)
                return false;
            result = (int)parsed;
            return true;
        }
        catch (Exception exception) when (
            exception is FormatException or
            InvalidCastException or
            OverflowException)
        {
            return false;
        }
    }

    private static ProcessResourceSnapshot Unavailable(string reason) =>
        new(
            false,
            reason,
            DateTimeOffset.UtcNow,
            Stopwatch.GetElapsedTime(0, Stopwatch.GetTimestamp()),
            Math.Max(1, Environment.ProcessorCount),
            []);

    private sealed record ProcessIdentity(
        int ProcessId,
        string Name,
        DateTimeOffset StartedAt,
        Process Process);

    private sealed record ParentProcessLink(
        int ParentProcessId,
        DateTimeOffset? StartedAt);

    private sealed record ParentProcessResult(
        bool IsAvailable,
        string? Error,
        IReadOnlyDictionary<int, int> Parents);
}
