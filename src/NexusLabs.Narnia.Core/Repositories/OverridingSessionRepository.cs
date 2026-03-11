using NexusLabs.Narnia.Core.Models;

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
    public async ValueTask<SessionSummary[]> ListRecentAsync(int limit = 20, CancellationToken ct = default)
    {
        var sessions = await inner.ListRecentAsync(limit, ct);
        return await MergeAllAsync(sessions, ct);
    }

    public async ValueTask<SessionSummary[]> ListByRepositoryAsync(string repository, CancellationToken ct = default)
    {
        var sessions = await inner.ListByRepositoryAsync(repository, ct);
        return await MergeAllAsync(sessions, ct);
    }

    public async ValueTask<SessionSummary[]> ListByCwdAsync(string cwd, CancellationToken ct = default)
    {
        var sessions = await inner.ListByCwdAsync(cwd, ct);
        return await MergeAllAsync(sessions, ct);
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

    public ValueTask<GlobalStats> GetGlobalStatsAsync(CancellationToken ct = default) =>
        inner.GetGlobalStatsAsync(ct);

    public ValueTask<ActivityDay[]> GetActivityByDateAsync(int days = 90, CancellationToken ct = default) =>
        inner.GetActivityByDateAsync(days, ct);

    public ValueTask<RepositoryStats[]> GetRepositoryStatsAsync(CancellationToken ct = default) =>
        inner.GetRepositoryStatsAsync(ct);

    public ValueTask<HotFile[]> GetHotFilesAsync(int limit = 20, CancellationToken ct = default) =>
        inner.GetHotFilesAsync(limit, ct);

    public ValueTask<FileHistoryEntry[]> GetFileHistoryAsync(string filePath, CancellationToken ct = default) =>
        inner.GetFileHistoryAsync(filePath, ct);

    public ValueTask<SessionSummary[]> GetSessionsByRefAsync(string refValue, CancellationToken ct = default) =>
        inner.GetSessionsByRefAsync(refValue, ct);

    public async ValueTask<ResumeSuggestion[]> GetResumeSuggestionsAsync(int limit = 10, CancellationToken ct = default)
    {
        var suggestions = await inner.GetResumeSuggestionsAsync(limit, ct);
        return await MergeAllAsync(suggestions, ct);
    }

    public ValueTask<KeywordFrequency[]> GetTopKeywordsAsync(int topN = 50, CancellationToken ct = default) =>
        inner.GetTopKeywordsAsync(topN, ct);

    // -------------------------------------------------------------------------
    private async ValueTask<SessionSummary[]> MergeAllAsync(SessionSummary[] sessions, CancellationToken ct)
    {
        var result = new SessionSummary[sessions.Length];
        for (var i = 0; i < sessions.Length; i++)
        {
            var ov = await overrides.GetOverrideAsync(sessions[i].Id, ct);
            result[i] = ov is null ? sessions[i] : Merge(sessions[i], ov);
        }

        return result;
    }

    private async ValueTask<ResumeSuggestion[]> MergeAllAsync(ResumeSuggestion[] suggestions, CancellationToken ct)
    {
        var result = new ResumeSuggestion[suggestions.Length];
        for (var i = 0; i < suggestions.Length; i++)
        {
            var ov = await overrides.GetOverrideAsync(suggestions[i].Session.Id, ct);
            result[i] = ov is null ? suggestions[i] : Merge(suggestions[i], ov);
        }

        return result;
    }

    private static Session Merge(Session s, SessionOverride ov) =>
        s with
        {
            Summary = ov.DisplayName ?? s.Summary,
            Repository = ov.Repository ?? s.Repository,
            Branch = ov.Branch ?? s.Branch,
        };

    private static SessionSummary Merge(SessionSummary s, SessionOverride ov) =>
        s with
        {
            Summary = ov.DisplayName ?? s.Summary,
            Repository = ov.Repository ?? s.Repository,
            Branch = ov.Branch ?? s.Branch,
        };

    private static ResumeSuggestion Merge(ResumeSuggestion s, SessionOverride ov) =>
        s with { Session = Merge(s.Session, ov) };
}
