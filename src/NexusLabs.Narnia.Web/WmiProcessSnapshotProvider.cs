using System.Diagnostics.CodeAnalysis;
using System.Management;
using System.Runtime.Versioning;
using NexusLabs.Narnia.Core.Models;
using NexusLabs.Narnia.Core.Repositories;

namespace NexusLabs.Narnia.Web;

/// <summary>
/// Windows process-snapshot provider backed by WMI. For efficiency it issues narrow queries —
/// processes carrying a <c>--resume</c> command line, <c>WindowsTerminal.exe</c> processes, and
/// <c>copilot.exe</c> agent processes — rather than enumerating every process on the machine.
/// A <c>copilot.exe</c> started without <c>--resume</c> (a freshly started session) carries no
/// session id anywhere, so its ancestor chain (node.exe, pwsh.exe, ...) is walked one PID at a
/// time to reach <c>WindowsTerminal.exe</c>, fetching only the missing links.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WmiProcessSnapshotProvider : IProcessSnapshotProvider
{
    private const string WindowsTerminalProcessName = "WindowsTerminal.exe";
    private const string CopilotAgentProcessName = "copilot.exe";
    private const int MaxAncestorWalk = 64;

    private const string ResumeCarrierQuery =
        "SELECT ProcessId, ParentProcessId, Name, CommandLine FROM Win32_Process WHERE CommandLine LIKE '%--resume%'";

    private const string TerminalQuery =
        "SELECT ProcessId, ParentProcessId, Name, CommandLine FROM Win32_Process WHERE Name = 'WindowsTerminal.exe'";

    private const string CopilotAgentQuery =
        "SELECT ProcessId, ParentProcessId, Name, CommandLine FROM Win32_Process WHERE Name = 'copilot.exe'";

    /// <inheritdoc />
    public IReadOnlyList<ProcessRecord> GetProcesses()
    {
        var byId = new Dictionary<int, ProcessRecord>();
        Collect(ResumeCarrierQuery, byId);
        Collect(TerminalQuery, byId);

        var copilotAgents = new Dictionary<int, ProcessRecord>();
        Collect(CopilotAgentQuery, copilotAgents);
        foreach (var (pid, record) in copilotAgents)
            byId[pid] = record;

        foreach (var agent in copilotAgents.Values)
            WalkAncestorsInto(agent, byId);

        return byId.Values.ToList();
    }

    /// <summary>
    /// Fetches just enough ancestor records (one targeted per-PID query per missing link) to
    /// let <see cref="Services.LiveWindowDetector"/> walk from <paramref name="start"/> up to its
    /// owning terminal, stopping as soon as an already-known process (from the other queries) or
    /// <c>WindowsTerminal.exe</c> is reached.
    /// </summary>
    private static void WalkAncestorsInto(ProcessRecord start, Dictionary<int, ProcessRecord> byId)
    {
        var current = start;
        for (var depth = 0; depth < MaxAncestorWalk; depth++)
        {
            if (string.Equals(current.Name, WindowsTerminalProcessName, StringComparison.OrdinalIgnoreCase))
                return;

            if (current.ParentProcessId == 0)
                return;

            if (byId.TryGetValue(current.ParentProcessId, out var known))
            {
                current = known;
                continue;
            }

            var parent = QuerySingle(current.ParentProcessId);
            if (parent is null)
                return;

            byId[parent.ProcessId] = parent;
            current = parent;
        }
    }

    private static ProcessRecord? QuerySingle(int processId)
    {
        var query = $"SELECT ProcessId, ParentProcessId, Name, CommandLine FROM Win32_Process WHERE ProcessId = {processId}";
        using var searcher = new ManagementObjectSearcher(query);
        using var results = searcher.Get();
        foreach (var item in results)
        {
            using var obj = item;
            if (TryReadProcess(obj, out var record))
                return record;
        }

        return null;
    }

    private static void Collect(string query, Dictionary<int, ProcessRecord> byId)
    {
        using var searcher = new ManagementObjectSearcher(query);
        using var results = searcher.Get();
        foreach (var item in results)
        {
            using var obj = item;
            if (TryReadProcess(obj, out var record))
                byId[record.ProcessId] = record;
        }
    }

    private static bool TryReadProcess(ManagementBaseObject obj, [NotNullWhen(true)] out ProcessRecord? record)
    {
        record = null;
        if (obj["ProcessId"] is not { } pidValue)
            return false;

        var processId = Convert.ToInt32(pidValue);
        var parentProcessId = obj["ParentProcessId"] is { } parentValue ? Convert.ToInt32(parentValue) : 0;
        var name = obj["Name"] as string ?? string.Empty;
        var commandLine = obj["CommandLine"] as string;

        record = new ProcessRecord(processId, parentProcessId, name, commandLine);
        return true;
    }
}
