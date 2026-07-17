using NexusLabs.Narnia.Core.Models;

namespace NexusLabs.Narnia.Core.Repositories;

/// <summary>
/// Adds Copilot-name and Narnia-alias matches to indexed content search and applies archive visibility.
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

        var sessionsTask = inner.GetSessionNamesAsync(ct).AsTask();
        var overridesTask = overrides.GetAllOverridesAsync(ct).AsTask();
        await Task.WhenAll(sessionsTask, overridesTask);

        var savedOverrides = await overridesTask;
        var nameResults = BuildNameResults(query, await sessionsTask, savedOverrides);
        var visibleNameResults = MergeResults(
            nameResults,
            [],
            savedOverrides,
            includeArchived).ToArray();
        if (limit > 0 && visibleNameResults.Length >= limit)
            return ApplyLimitAndNormalizeScores(visibleNameResults, limit);

        var archivedCount = includeArchived
            ? 0
            : savedOverrides.Values.Count(sessionOverride => sessionOverride.IsArchived);
        var contentLimit = limit < 0
            ? int.MaxValue
            : (int)Math.Min(int.MaxValue, (long)limit + archivedCount);
        var contentResults = await inner.SearchAsync(
            query,
            contentLimit,
            includeArchived: true,
            ct: ct);
        var mergedResults = MergeResults(
            visibleNameResults,
            contentResults,
            savedOverrides,
            includeArchived);
        return ApplyLimitAndNormalizeScores(mergedResults, limit);
    }

    private static IEnumerable<SearchResult> BuildNameResults(
        string query,
        IReadOnlyList<SessionNameRecord> sessions,
        IReadOnlyDictionary<string, SessionOverride> savedOverrides)
    {
        var nameQuery = NormalizeNameQuery(query);
        if (nameQuery.Length == 0)
            return [];

        var matches =
            new List<(SearchResult Result, int Tier, int SourcePriority, DateTimeOffset UpdatedAt)>();
        foreach (var session in sessions)
        {
            savedOverrides.TryGetValue(session.SessionId, out var sessionOverride);
            var alias = sessionOverride?.DisplayName;
            if (!string.IsNullOrWhiteSpace(alias)
                && !string.Equals(alias, session.Name, StringComparison.OrdinalIgnoreCase)
                && MatchTier(alias, nameQuery) is { } aliasTier)
            {
                matches.Add((
                    new SearchResult(session.SessionId, "narnia_alias", null, alias, aliasTier),
                    aliasTier,
                    0,
                    session.UpdatedAt));
            }

            if (!string.IsNullOrWhiteSpace(session.Name)
                && MatchTier(session.Name, nameQuery) is { } nameTier)
            {
                matches.Add((
                    new SearchResult(session.SessionId, "session_name", null, session.Name, nameTier),
                    nameTier,
                    1,
                    session.UpdatedAt));
            }
        }

        return matches
            .OrderBy(match => match.Tier)
            .ThenBy(match => match.SourcePriority)
            .ThenByDescending(match => match.UpdatedAt)
            .ThenBy(match => match.Result.SessionId, StringComparer.Ordinal)
            .Select(match => match.Result);
    }

    private static IEnumerable<SearchResult> MergeResults(
        IEnumerable<SearchResult> nameResults,
        IEnumerable<SearchResult> contentResults,
        IReadOnlyDictionary<string, SessionOverride> savedOverrides,
        bool includeArchived)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var result in nameResults.Concat(contentResults))
        {
            savedOverrides.TryGetValue(result.SessionId, out var sessionOverride);
            if (!includeArchived && sessionOverride?.IsArchived == true)
                continue;
            if (seen.Add(result.SessionId))
                yield return result;
        }
    }

    private static int? MatchTier(string name, string query)
    {
        if (string.Equals(name, query, StringComparison.OrdinalIgnoreCase))
            return 0;
        if (name.StartsWith(query, StringComparison.OrdinalIgnoreCase))
            return 1;
        if (name.Contains(query, StringComparison.OrdinalIgnoreCase))
            return 2;
        return null;
    }

    private static string NormalizeNameQuery(string query)
    {
        var normalized = query.Trim();
        if (normalized.Length >= 2
            && ((normalized[0] == '"' && normalized[^1] == '"')
                || (normalized[0] == '\'' && normalized[^1] == '\'')))
        {
            normalized = normalized[1..^1].Trim();
        }
        if (normalized.EndsWith('*'))
            normalized = normalized.TrimEnd('*').TrimEnd();
        return normalized;
    }

    private static SearchResult[] ApplyLimitAndNormalizeScores(
        IEnumerable<SearchResult> results,
        int limit)
    {
        var limitedResults = limit < 0
            ? results.ToArray()
            : results.Take(limit).ToArray();
        return limitedResults
            .Select((result, index) => result with { Score = index })
            .ToArray();
    }
}
