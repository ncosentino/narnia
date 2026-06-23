using NexusLabs.Narnia.Core.Models;

namespace NexusLabs.Narnia.Core.Repositories;

/// <summary>
/// Persists recorded terminal windows of Copilot tabs in the Narnia settings database.
/// Open windows are tracked by terminal process id; closed windows are retained
/// (deduplicated by composition) so a whole window can be reopened after it is lost.
/// </summary>
public interface ITerminalWindowsRepository
{
    /// <summary>Returns all currently-open windows, each with its tabs in tab order.</summary>
    ValueTask<IReadOnlyList<TerminalWindow>> GetOpenAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns the most recently closed windows, newest first, up to <paramref name="limit"/>.
    /// </summary>
    ValueTask<IReadOnlyList<TerminalWindow>> GetClosedAsync(int limit, CancellationToken ct = default);

    /// <summary>Returns a single window by id, or <c>null</c> if it does not exist.</summary>
    ValueTask<TerminalWindow?> GetByIdAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// Inserts or updates an open window keyed by its terminal process id. When a record
    /// with the same live process id already exists it is refreshed in place (tabs,
    /// composition, recency); otherwise a new open record is created.
    /// </summary>
    /// <param name="terminalProcessId">The owning terminal process id.</param>
    /// <param name="compositionKey">The composition key for the window's session set.</param>
    /// <param name="tabs">The window's tabs in tab order.</param>
    /// <param name="now">The current timestamp.</param>
    /// <param name="ct">A cancellation token.</param>
    ValueTask UpsertOpenAsync(
        int terminalProcessId,
        string compositionKey,
        IReadOnlyList<TerminalWindowTab> tabs,
        DateTimeOffset now,
        CancellationToken ct = default);

    /// <summary>
    /// Marks the open window with the given id as closed. If another closed window already
    /// has the same composition, this window is merged into it (incrementing the occurrence
    /// count and refreshing recency and tabs) instead of leaving a duplicate.
    /// </summary>
    ValueTask CloseAsync(string id, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>Sets a window's display name and pinned state. Pinned windows are never pruned.</summary>
    ValueTask SetNameAsync(string id, string? name, bool pinned, CancellationToken ct = default);

    /// <summary>Deletes a window and its tabs.</summary>
    ValueTask DeleteAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// Removes non-pinned closed windows beyond the most recent <paramref name="keepCount"/>
    /// (ordered by recency), bounding how much closed history is retained.
    /// </summary>
    ValueTask PruneClosedAsync(int keepCount, CancellationToken ct = default);
}
