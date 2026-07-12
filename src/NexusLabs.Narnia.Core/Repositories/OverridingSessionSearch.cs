using NexusLabs.Narnia.Core.Models;

namespace NexusLabs.Narnia.Core.Repositories;

/// <summary>
/// Applies Narnia archive visibility to ranked session-content search results.
/// </summary>
public sealed class OverridingSessionSearch(
    SqliteSessionRepository inner,
    ISessionOverridesRepository overrides) : ISessionSearch
{
    /// <inheritdoc />
    public ValueTask<SearchResult[]> SearchAsync(
        string query,
        int limit = 20,
        CancellationToken ct = default) =>
        SearchAsync(query, limit, includeArchived: false, ct);

    /// <inheritdoc />
    public async ValueTask<SearchResult[]> SearchAsync(
        string query,
        int limit,
        bool includeArchived,
        CancellationToken ct = default)
    {
        if (limit == 0)
            return [];

        var resultsTask = inner.SearchAsync(
            query,
            int.MaxValue,
            includeArchived: true,
            ct: ct).AsTask();

        if (includeArchived)
        {
            var results = await resultsTask;
            return ApplyLimit(results, limit);
        }

        var archivedIdsTask = overrides.GetArchivedSessionIdsAsync(ct).AsTask();
        await Task.WhenAll(resultsTask, archivedIdsTask);

        var archivedIds = await archivedIdsTask;
        var visibleResults = (await resultsTask)
            .Where(result => !archivedIds.Contains(result.SessionId));
        return ApplyLimit(visibleResults, limit);
    }

    private static SearchResult[] ApplyLimit(
        IEnumerable<SearchResult> results,
        int limit) =>
        limit < 0
            ? results.ToArray()
            : results.Take(limit).ToArray();
}
