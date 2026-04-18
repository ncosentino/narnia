using NexusLabs.Narnia.Core.Models;

namespace NexusLabs.Narnia.Core.Repositories;

public interface ISessionRepository
{
    ValueTask<SessionSummary[]> ListRecentAsync(int limit = 20, bool includeArchived = false, CancellationToken ct = default);
    ValueTask<SessionSummary[]> ListByRepositoryAsync(string repository, bool includeArchived = false, CancellationToken ct = default);
    ValueTask<SessionSummary[]> ListByCwdAsync(string cwd, bool includeArchived = false, CancellationToken ct = default);
    ValueTask<Session?> GetByIdAsync(string sessionId, CancellationToken ct = default);
    ValueTask<Turn[]> GetTurnsAsync(string sessionId, int offset = 0, int limit = 50, CancellationToken ct = default);
    ValueTask<Checkpoint[]> GetCheckpointsAsync(string sessionId, CancellationToken ct = default);
    ValueTask<SessionFile[]> GetFilesAsync(string sessionId, CancellationToken ct = default);
    ValueTask<SessionRef[]> GetRefsAsync(string sessionId, CancellationToken ct = default);
    ValueTask<GlobalStats> GetGlobalStatsAsync(CancellationToken ct = default);
    ValueTask<ActivityDay[]> GetActivityByDateAsync(int days = 90, CancellationToken ct = default);
    ValueTask<RepositoryStats[]> GetRepositoryStatsAsync(CancellationToken ct = default);
    ValueTask<HotFile[]> GetHotFilesAsync(int limit = 20, CancellationToken ct = default);
    ValueTask<FileHistoryEntry[]> GetFileHistoryAsync(string filePath, CancellationToken ct = default);
    ValueTask<SessionSummary[]> GetSessionsByRefAsync(string refValue, CancellationToken ct = default);
    ValueTask<ResumeSuggestion[]> GetResumeSuggestionsAsync(int limit = 10, CancellationToken ct = default);
    ValueTask<Dictionary<string, string>> GetResumableSessionIdsAsync(IReadOnlyList<string> sessionIds, CancellationToken ct = default);
    ValueTask<KeywordFrequency[]> GetTopKeywordsAsync(int topN = 50, CancellationToken ct = default);
}
