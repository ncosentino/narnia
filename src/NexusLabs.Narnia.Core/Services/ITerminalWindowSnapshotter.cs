namespace NexusLabs.Narnia.Core.Services;

/// <summary>
/// Performs a single reconciliation pass: detect the live terminal windows, persist the
/// open ones, mark vanished ones closed, and prune old closed history. Separated from any
/// hosting/timer concern so the reconciliation logic is unit-testable.
/// </summary>
public interface ITerminalWindowSnapshotter
{
    /// <summary>
    /// Runs one snapshot pass.
    /// </summary>
    /// <param name="now">The timestamp to record for this pass.</param>
    /// <param name="retentionCount">Number of most-recent closed windows to retain after pruning.</param>
    /// <param name="ct">A cancellation token.</param>
    ValueTask SnapshotAsync(DateTimeOffset now, int retentionCount, CancellationToken ct = default);
}
