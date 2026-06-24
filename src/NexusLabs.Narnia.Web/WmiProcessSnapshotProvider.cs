using System.Diagnostics.CodeAnalysis;
using System.Management;
using System.Runtime.Versioning;
using NexusLabs.Narnia.Core.Models;
using NexusLabs.Narnia.Core.Repositories;

namespace NexusLabs.Narnia.Web;

/// <summary>
/// Windows process-snapshot provider backed by WMI. For efficiency it issues two narrow
/// queries — processes carrying a <c>--resume</c> command line, and <c>WindowsTerminal.exe</c>
/// processes — whose union is closed under the parent walk to the owning terminal.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WmiProcessSnapshotProvider : IProcessSnapshotProvider
{
    private const string ResumeCarrierQuery =
        "SELECT ProcessId, ParentProcessId, Name, CommandLine FROM Win32_Process WHERE CommandLine LIKE '%--resume%'";

    private const string TerminalQuery =
        "SELECT ProcessId, ParentProcessId, Name, CommandLine FROM Win32_Process WHERE Name = 'WindowsTerminal.exe'";

    /// <inheritdoc />
    public IReadOnlyList<ProcessRecord> GetProcesses()
    {
        var byId = new Dictionary<int, ProcessRecord>();
        Collect(ResumeCarrierQuery, byId);
        Collect(TerminalQuery, byId);
        return byId.Values.ToList();
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
