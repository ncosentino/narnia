using NexusLabs.Narnia.Core.Models;
using System.Linq;

namespace NexusLabs.Narnia.Core.Repositories;

/// <summary>
/// Decorator over <see cref="ISessionRepository"/> that merges user-defined overrides (display_name,
/// repository, branch) from <see cref="ISessionOverridesRepository"/> into every returned
/// <see cref="Session"/> and <see cref="SessionSummary"/>.
/// </summary>
public sealed class OverridingSessionRepository(
    SqliteSessionRepository inner,
    ISessionOverridesRepository overrides) : ISessionRepository
{
    /// <inheritdoc />
    public async ValueTask<SessionSummary[]> ListAllAsync(bool includeArchived = false, CancellationToken ct = default)
    {
        var sessions = await inner.ListAllAsync(includeArchived: true, ct);
        return await MergeAllAsync(sessions, includeArchived, ct);
    }

    public async ValueTask<SessionSummary[]> ListRecentAsync(int limit = 20, bool includeArchived = false, CancellationToken ct = default)
    {
        var sessions = await ListAllAsync(includeArchived, ct);
        return limit < 0 ? sessions : sessions.Take(limit).ToArray();
    }

    public async ValueTask<SessionSummary[]> ListByRepositoryAsync(string repository, bool includeArchived = false, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(repository))
            return [];

        var sessions = await ListAllAsync(includeArchived, ct);
        return sessions
            .Where(session => string.Equals(
                session.Repository,
                repository.Trim(),
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    public async ValueTask<SessionSummary[]> ListByCwdAsync(string cwd, bool includeArchived = false, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(cwd))
            return [];

        var sessions = await ListAllAsync(includeArchived, ct);
        return sessions
            .Where(session => PathsEqual(session.Cwd, cwd))
            .ToArray();
    }

    public async ValueTask<Session?> GetByIdAsync(string sessionId, CancellationToken ct = default)
    {
        var session = await inner.GetByIdAsync(sessionId, ct);
        if (session is null)
            return null;

        var ov = await overrides.GetOverrideAsync(sessionId, ct);
        return ov is null ? session : Merge(session, ov);
    }

    // Pass-through methods — no session identity fields to merge
    public ValueTask<Turn[]> GetTurnsAsync(string sessionId, int offset = 0, int limit = 50, CancellationToken ct = default) =>
        inner.GetTurnsAsync(sessionId, offset, limit, ct);

    public ValueTask<Checkpoint[]> GetCheckpointsAsync(string sessionId, CancellationToken ct = default) =>
        inner.GetCheckpointsAsync(sessionId, ct);

    public ValueTask<SessionFile[]> GetFilesAsync(string sessionId, CancellationToken ct = default) =>
        inner.GetFilesAsync(sessionId, ct);

    public ValueTask<SessionRef[]> GetRefsAsync(string sessionId, CancellationToken ct = default) =>
        inner.GetRefsAsync(sessionId, ct);

    public async ValueTask<GlobalStats> GetGlobalStatsAsync(CancellationToken ct = default)
    {
        var statsTask = inner.GetGlobalStatsAsync(ct).AsTask();
        var repositoryStatsTask = GetRepositoryStatsAsync(ct).AsTask();
        await Task.WhenAll(statsTask, repositoryStatsTask);

        var stats = await statsTask;
        var repositoryStats = await repositoryStatsTask;
        return stats with
        {
            MostActiveRepository = repositoryStats.FirstOrDefault()?.Repository,
        };
    }

    public ValueTask<ActivityDay[]> GetActivityByDateAsync(int days = 90, CancellationToken ct = default) =>
        inner.GetActivityByDateAsync(days, ct);

    /// <inheritdoc />
    public ValueTask<ActivityTimelineDay[]> GetActivityTimelineAsync(int days = 90, CancellationToken ct = default) =>
        inner.GetActivityTimelineAsync(days, ct);

    /// <inheritdoc />
    public ValueTask<SessionActivitySource[]> GetSessionActivitySourcesAsync(
        DateOnly date,
        CancellationToken ct = default) =>
        inner.GetSessionActivitySourcesAsync(date, ct);

    /// <inheritdoc />
    public async ValueTask<SessionSummary[]> ListByActivitySourceAsync(
        SessionActivitySourceFilter filter,
        bool includeArchived = false,
        CancellationToken ct = default)
    {
        var sessions = await inner.ListByActivitySourceAsync(filter, includeArchived, ct);
        return await MergeAllAsync(sessions, includeArchived, ct);
    }

    public async ValueTask<RepositoryStats[]> GetRepositoryStatsAsync(CancellationToken ct = default)
    {
        var sessionsTask = ListAllAsync(includeArchived: false, ct).AsTask();
        var fileCountsTask = inner.GetFileCountsBySessionAsync(ct).AsTask();
        await Task.WhenAll(sessionsTask, fileCountsTask);

        var sessions = await sessionsTask;
        var fileCounts = await fileCountsTask;

        return [.. sessions
            .Where(session => !string.IsNullOrWhiteSpace(session.Repository))
            .GroupBy(session => session.Repository!, StringComparer.OrdinalIgnoreCase)
            .Select(group => new RepositoryStats(
                group.Key,
                group.Count(),
                group.Sum(session => session.TurnCount),
                group.Sum(session => fileCounts.GetValueOrDefault(session.Id)),
                group.Max(session => session.UpdatedAt)))
            .OrderByDescending(stats => stats.SessionCount)
            .ThenBy(stats => stats.Repository, StringComparer.OrdinalIgnoreCase)];
    }

    public async ValueTask<SessionInsights> GetSessionInsightsAsync(CancellationToken ct = default)
    {
        var insightsTask = inner.GetSessionInsightsAsync(ct).AsTask();
        var sessionsTask = ListAllAsync(includeArchived: false, ct).AsTask();
        await Task.WhenAll(insightsTask, sessionsTask);

        var insights = await insightsTask;
        var sessions = await sessionsTask;
        return insights with
        {
            DistinctRepositories = sessions
                .Select(session => session.Repository)
                .Where(repository => !string.IsNullOrWhiteSpace(repository))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count(),
            DistinctBranches = sessions
                .Select(session => session.Branch)
                .Where(branch => !string.IsNullOrWhiteSpace(branch))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count(),
        };
    }

    public ValueTask<ActivityPatterns> GetActivityPatternsAsync(CancellationToken ct = default) =>
        inner.GetActivityPatternsAsync(ct);

    public ValueTask<HotFile[]> GetHotFilesAsync(int limit = 20, CancellationToken ct = default) =>
        inner.GetHotFilesAsync(limit, ct);

    public ValueTask<HotFile[]> SearchFilesAsync(string query, int limit = 100, CancellationToken ct = default) =>
        inner.SearchFilesAsync(query, limit, ct);

    public async ValueTask<FileHistoryEntry[]> GetFileHistoryAsync(string filePath, CancellationToken ct = default)
    {
        var entries = await inner.GetFileHistoryAsync(filePath, ct);
        var savedOverrides = await overrides.GetAllOverridesAsync(ct);
        return [.. entries.Select(entry =>
        {
            savedOverrides.TryGetValue(entry.SessionId, out var sessionOverride);
            return sessionOverride is null
                ? entry
                : entry with
                {
                    Summary = sessionOverride.DisplayName ?? entry.Summary,
                    IsFavorite = sessionOverride.IsFavorite,
                    RecordedSummary = sessionOverride.DisplayName is null ? null : entry.Summary,
                };
        })];
    }

    public async ValueTask<CommitMatch[]> GetSessionsByRefAsync(string refValue, CancellationToken ct = default)
    {
        var matches = await inner.GetSessionsByRefAsync(refValue, ct);
        return await MergeAllAsync(matches, ct);
    }

    public async ValueTask<ResumeSuggestion[]> GetResumeSuggestionsAsync(int limit = 10, CancellationToken ct = default)
    {
        var suggestions = await inner.GetResumeSuggestionsAsync(limit, ct);
        return await MergeAllAsync(suggestions, ct);
    }

    public ValueTask<Dictionary<string, string>> GetResumableSessionIdsAsync(IReadOnlyList<string> sessionIds, CancellationToken ct = default) =>
        inner.GetResumableSessionIdsAsync(sessionIds, ct);

    public ValueTask<KeywordFrequency[]> GetTopKeywordsAsync(int topN = 50, CancellationToken ct = default) =>
        inner.GetTopKeywordsAsync(topN, ct);

    // -------------------------------------------------------------------------
    private async ValueTask<SessionSummary[]> MergeAllAsync(SessionSummary[] sessions, bool includeArchived, CancellationToken ct)
    {
        var savedOverrides = await overrides.GetAllOverridesAsync(ct);
        var result = new List<SessionSummary>(sessions.Length);
        foreach (var session in sessions)
        {
            savedOverrides.TryGetValue(session.Id, out var sessionOverride);
            if (!includeArchived && sessionOverride?.IsArchived == true)
                continue;

            result.Add(sessionOverride is null ? session : Merge(session, sessionOverride));
        }

        return [.. result];
    }

    private async ValueTask<ResumeSuggestion[]> MergeAllAsync(ResumeSuggestion[] suggestions, CancellationToken ct)
    {
        var savedOverrides = await overrides.GetAllOverridesAsync(ct);
        var result = new ResumeSuggestion[suggestions.Length];
        for (var i = 0; i < suggestions.Length; i++)
        {
            savedOverrides.TryGetValue(suggestions[i].Session.Id, out var sessionOverride);
            result[i] = sessionOverride is null ? suggestions[i] : Merge(suggestions[i], sessionOverride);
        }

        return result;
    }

    // No includeArchived filtering here, matching GetResumeSuggestionsAsync/MergeAllAsync above:
    // a targeted ref lookup should still surface an archived session's match rather than hide it.
    private async ValueTask<CommitMatch[]> MergeAllAsync(CommitMatch[] matches, CancellationToken ct)
    {
        var savedOverrides = await overrides.GetAllOverridesAsync(ct);
        var result = new CommitMatch[matches.Length];
        for (var i = 0; i < matches.Length; i++)
        {
            savedOverrides.TryGetValue(matches[i].Session.Id, out var sessionOverride);
            result[i] = sessionOverride is null ? matches[i] : Merge(matches[i], sessionOverride);
        }

        return result;
    }

    private static Session Merge(Session s, SessionOverride ov) =>
        s with
        {
            Summary = ov.DisplayName ?? s.Summary,
            Repository = ov.Repository ?? s.Repository,
            Branch = ov.Branch ?? s.Branch,
            IsFavorite = ov.IsFavorite,
        };

    private static SessionSummary Merge(SessionSummary s, SessionOverride ov) =>
        s with
        {
            Summary = ov.DisplayName ?? s.Summary,
            Repository = ov.Repository ?? s.Repository,
            Branch = ov.Branch ?? s.Branch,
            IsFavorite = ov.IsFavorite,
        };

    private static ResumeSuggestion Merge(ResumeSuggestion s, SessionOverride ov) =>
        s with { Session = Merge(s.Session, ov) };

    private static CommitMatch Merge(CommitMatch m, SessionOverride ov) =>
        m with { Session = Merge(m.Session, ov) };

    private static bool PathsEqual(string? left, string right)
    {
        if (left is null)
            return false;

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(
            Path.TrimEndingDirectorySeparator(left),
            Path.TrimEndingDirectorySeparator(right.Trim()),
            comparison);
    }
}
