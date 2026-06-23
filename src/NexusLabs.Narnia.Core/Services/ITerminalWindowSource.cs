using NexusLabs.Narnia.Core.Models;

namespace NexusLabs.Narnia.Core.Services;

/// <summary>
/// An open/closed view of terminal windows contributed by a single source.
/// </summary>
/// <param name="Open">Currently-open windows.</param>
/// <param name="Closed">Recently-closed windows.</param>
public sealed record TerminalWindowSnapshot(
    IReadOnlyList<TerminalWindow> Open,
    IReadOnlyList<TerminalWindow> Closed);

/// <summary>
/// A source of recoverable terminal windows for the recovery console. The live snapshotter is
/// one source; additional sources (for example a future launch-history source) can contribute
/// windows without changing the console. Sources expose only the domain
/// <see cref="TerminalWindow"/> model — how a source stores or represents its windows is its own
/// private concern and never visible on this contract.
/// </summary>
public interface ITerminalWindowSource
{
    /// <summary>A stable identifier for the source (e.g. <c>"live"</c>).</summary>
    string SourceId { get; }

    /// <summary>
    /// Returns this source's open and recently-closed windows.
    /// </summary>
    /// <param name="closedLimit">Maximum number of closed windows to return.</param>
    /// <param name="ct">A cancellation token.</param>
    ValueTask<TerminalWindowSnapshot> GetWindowsAsync(int closedLimit, CancellationToken ct = default);
}
