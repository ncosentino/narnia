namespace NexusLabs.Narnia.Core.Services;

/// <summary>
/// Combines all registered <see cref="ITerminalWindowSource"/>s into a single view for the
/// recovery console, so new sources can contribute windows without changing consumers.
/// </summary>
public interface ITerminalWindowAggregator
{
    /// <summary>
    /// Returns the merged open and recently-closed windows across every source, newest first.
    /// </summary>
    /// <param name="closedLimit">Maximum number of closed windows to return overall.</param>
    /// <param name="ct">A cancellation token.</param>
    ValueTask<TerminalWindowSnapshot> GetWindowsAsync(int closedLimit, CancellationToken ct = default);
}
