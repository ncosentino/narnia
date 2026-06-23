using NexusLabs.Narnia.Core.Models;

namespace NexusLabs.Narnia.Core.Repositories;

/// <summary>
/// Supplies a point-in-time snapshot of running processes for terminal-window detection.
/// Implementations are platform-specific (e.g. WMI on Windows); callers depend only on
/// this contract so the detection logic stays platform-neutral and unit-testable.
/// </summary>
public interface IProcessSnapshotProvider
{
    /// <summary>
    /// Returns the current set of processes relevant to terminal-window detection. An
    /// implementation may scope the result (for efficiency) to terminal processes and
    /// those carrying a <c>--resume</c> command line, provided the set remains closed
    /// under the parent walk up to the owning terminal.
    /// </summary>
    IReadOnlyList<ProcessRecord> GetProcesses();
}
