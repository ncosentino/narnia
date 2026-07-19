using NexusLabs.Narnia.Core.Models;

namespace NexusLabs.Narnia.Core.Services;

/// <summary>Measures local Copilot session-state directories without reading file contents.</summary>
public interface ISessionStorageScanner
{
    /// <summary>Scans every current local session-state directory and persists the resulting cache.</summary>
    /// <param name="progress">Receives completed-session progress updates.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>Current per-session measurements.</returns>
    ValueTask<IReadOnlyList<SessionStorageRecord>> ScanAsync(
        IProgress<(int Scanned, int Total)> progress,
        CancellationToken ct);
}
