namespace NexusLabs.Narnia.Core.Models;

/// <summary>
/// A minimal, platform-neutral snapshot of a single running process, carrying only
/// the fields needed to reconstruct terminal windows: identity, parentage, image
/// name, and the command line (which is where <c>copilot --resume=&lt;id&gt;</c> lives).
/// </summary>
/// <param name="ProcessId">The operating-system process id.</param>
/// <param name="ParentProcessId">The process id of the parent, used to walk to the owning terminal.</param>
/// <param name="Name">The process image name (e.g. <c>pwsh.exe</c>, <c>WindowsTerminal.exe</c>).</param>
/// <param name="CommandLine">The full command line, or <c>null</c> when it could not be read.</param>
public sealed record ProcessRecord(
    int ProcessId,
    int ParentProcessId,
    string Name,
    string? CommandLine);
