using NexusLabs.Narnia.Core.Models;

namespace NexusLabs.Narnia.Core.Services;

/// <summary>
/// Produces live process diagnostics with Copilot session and terminal ownership.
/// </summary>
public interface IProcessDiagnosticsService
{
    /// <summary>
    /// Gets the latest sampled diagnostics. Concurrent callers share one sampling baseline.
    /// </summary>
    /// <param name="ct">Cancellation token for process capture and session lookup.</param>
    /// <returns>The current diagnostics snapshot.</returns>
    ValueTask<ProcessDiagnosticsSnapshot> GetSnapshotAsync(CancellationToken ct = default);
}
