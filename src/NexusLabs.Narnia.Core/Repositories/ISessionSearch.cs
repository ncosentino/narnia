using NexusLabs.Narnia.Core.Models;

namespace NexusLabs.Narnia.Core.Repositories;

/// <summary>
/// Searches indexed Copilot session content.
/// </summary>
public interface ISessionSearch
{
    /// <summary>
    /// Searches visible session content and returns the strongest match from each session.
    /// </summary>
    /// <param name="query">Content query to execute.</param>
    /// <param name="limit">Maximum number of sessions to return.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>Ranked search results.</returns>
    ValueTask<SearchResult[]> SearchAsync(string query, int limit = 20, CancellationToken ct = default);

    /// <summary>
    /// Searches session content and optionally includes sessions archived through Narnia.
    /// </summary>
    /// <param name="query">Content query to execute.</param>
    /// <param name="limit">Maximum number of sessions to return.</param>
    /// <param name="includeArchived">Whether archived sessions should be included.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>Ranked search results.</returns>
    ValueTask<SearchResult[]> SearchAsync(
        string query,
        int limit,
        bool includeArchived,
        CancellationToken ct = default);
}
