using NexusLabs.Narnia.Core.Models;

namespace NexusLabs.Narnia.Core.Repositories;

public interface ISessionRepository
{
    /// <summary>
    /// Lists every recorded session, ordered from most recently updated to least recently updated.
    /// </summary>
    /// <param name="includeArchived">Whether sessions archived through Narnia should be included.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>All matching session summaries.</returns>
    ValueTask<SessionSummary[]> ListAllAsync(bool includeArchived = false, CancellationToken ct = default);

    /// <summary>
    /// Lists the most recently updated visible sessions.
    /// </summary>
    /// <param name="limit">Maximum number of sessions to return. A negative value returns all sessions.</param>
    /// <param name="includeArchived">Whether sessions archived through Narnia should be included.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>Session summaries ordered from most recently updated to least recently updated.</returns>
    ValueTask<SessionSummary[]> ListRecentAsync(int limit = 20, bool includeArchived = false, CancellationToken ct = default);

    /// <summary>
    /// Lists sessions whose effective remote repository exactly matches the requested value.
    /// </summary>
    /// <param name="repository">Repository in <c>owner/repository</c> form.</param>
    /// <param name="includeArchived">Whether sessions archived through Narnia should be included.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>Matching session summaries ordered by most recent update.</returns>
    ValueTask<SessionSummary[]> ListByRepositoryAsync(string repository, bool includeArchived = false, CancellationToken ct = default);

    /// <summary>
    /// Lists sessions whose recorded working directory exactly matches the requested path.
    /// </summary>
    /// <param name="cwd">Working directory path to match.</param>
    /// <param name="includeArchived">Whether sessions archived through Narnia should be included.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>Matching session summaries ordered by most recent update.</returns>
    ValueTask<SessionSummary[]> ListByCwdAsync(string cwd, bool includeArchived = false, CancellationToken ct = default);
    ValueTask<Session?> GetByIdAsync(string sessionId, CancellationToken ct = default);
    ValueTask<Turn[]> GetTurnsAsync(string sessionId, int offset = 0, int limit = 50, CancellationToken ct = default);
    ValueTask<Checkpoint[]> GetCheckpointsAsync(string sessionId, CancellationToken ct = default);
    ValueTask<SessionFile[]> GetFilesAsync(string sessionId, CancellationToken ct = default);
    ValueTask<SessionRef[]> GetRefsAsync(string sessionId, CancellationToken ct = default);

    /// <summary>
    /// Gets global session statistics with repository-derived values based on effective visible metadata.
    /// </summary>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>Global session statistics.</returns>
    ValueTask<GlobalStats> GetGlobalStatsAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets daily session-creation counts for the requested recent window.
    /// </summary>
    /// <param name="days">Number of days before today to include.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>Session counts ordered from earliest to latest activity date.</returns>
    ValueTask<ActivityDay[]> GetActivityByDateAsync(int days = 90, CancellationToken ct = default);

    /// <summary>
    /// Gets daily counts for sessions, turns, first-observed files, and checkpoints.
    /// </summary>
    /// <param name="days">Number of days before today to include.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>Activity counts ordered from earliest to latest activity date.</returns>
    ValueTask<ActivityTimelineDay[]> GetActivityTimelineAsync(int days = 90, CancellationToken ct = default);

    /// <summary>
    /// Gets recorded session provenance grouped by repository, host, or normalized working
    /// directory for one local calendar date.
    /// </summary>
    /// <param name="date">Local calendar date to inspect.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>Source groups ordered by descending raw session count.</returns>
    ValueTask<SessionActivitySource[]> GetSessionActivitySourcesAsync(
        DateOnly date,
        CancellationToken ct = default);

    /// <summary>
    /// Lists the sessions represented by an activity source row using recorded provenance values
    /// before applying display overrides.
    /// </summary>
    /// <param name="filter">Exact recorded source and local-date filter.</param>
    /// <param name="includeArchived">Whether archived sessions are included.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>Matching session summaries ordered by most recent update.</returns>
    ValueTask<SessionSummary[]> ListByActivitySourceAsync(
        SessionActivitySourceFilter filter,
        bool includeArchived = false,
        CancellationToken ct = default);

    /// <summary>
    /// Gets per-repository statistics after applying Narnia overrides and excluding archived sessions.
    /// </summary>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>Repository statistics ordered by descending visible session count.</returns>
    ValueTask<RepositoryStats[]> GetRepositoryStatsAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets session insights with repository and branch counts based on effective visible metadata.
    /// </summary>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>Aggregated session insights.</returns>
    ValueTask<SessionInsights> GetSessionInsightsAsync(CancellationToken ct = default);
    ValueTask<ActivityPatterns> GetActivityPatternsAsync(CancellationToken ct = default);
    ValueTask<HotFile[]> GetHotFilesAsync(int limit = 20, CancellationToken ct = default);

    /// <summary>
    /// Gets enriched file hotspots with project or generated-data context.
    /// </summary>
    /// <param name="perCategoryLimit">Maximum identities returned for each category.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>Top contextual hotspots and complete category counts.</returns>
    ValueTask<FileHotspotSummary> GetFileHotspotsAsync(
        int perCategoryLimit = 25,
        CancellationToken ct = default);

    /// <summary>
    /// Searches recorded file paths using case-insensitive, separator-normalized substring matching.
    /// </summary>
    /// <param name="query">Path fragment to search for. An empty value returns recently recorded paths.</param>
    /// <param name="limit">Maximum number of path summaries to return.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>Matching recorded path summaries.</returns>
    ValueTask<HotFile[]> SearchFilesAsync(string query, int limit = 100, CancellationToken ct = default);

    ValueTask<FileHistoryEntry[]> GetFileHistoryAsync(string filePath, CancellationToken ct = default);
    ValueTask<CommitMatch[]> GetSessionsByRefAsync(string refValue, CancellationToken ct = default);
    /// <summary>
    /// Gets sessions whose latest checkpoint records next steps, ordered by most recent activity.
    /// </summary>
    /// <param name="limit">Maximum suggestions to return. A negative value returns all suggestions.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>Sessions with checkpoint-based continuation context.</returns>
    ValueTask<ResumeSuggestion[]> GetResumeSuggestionsAsync(int limit = 10, CancellationToken ct = default);
    ValueTask<Dictionary<string, string>> GetResumableSessionIdsAsync(IReadOnlyList<string> sessionIds, CancellationToken ct = default);
    ValueTask<KeywordFrequency[]> GetTopKeywordsAsync(int topN = 50, CancellationToken ct = default);
}
