using Microsoft.Data.Sqlite;
using NexusLabs.Narnia.Core.Configuration;
using NexusLabs.Narnia.Core.Models;

namespace NexusLabs.Narnia.Core.Repositories;

public sealed class SqliteSessionRepository(NarniaOptions options) : ISessionRepository, ISessionSearch
{
    private readonly string _connectionString = options.ConnectionString
        ?? $"Data Source={options.DatabasePath};Mode=ReadOnly";

    private const string SessionSummarySelectSql =
        """
        SELECT s.id, s.cwd, s.repository, s.branch, s.summary, s.created_at, s.updated_at,
               COUNT(DISTINCT t.id) as turn_count, COUNT(DISTINCT c.id) as checkpoint_count
        FROM sessions s
        LEFT JOIN turns t ON t.session_id = s.id
        LEFT JOIN checkpoints c ON c.session_id = s.id
        """;

    private static readonly string ListAllSql =
        $"""
        {SessionSummarySelectSql}
        GROUP BY s.id
        ORDER BY s.updated_at DESC, s.id
        """;

    private static readonly string ListRecentSql =
        $"""
        {SessionSummarySelectSql}
        GROUP BY s.id
        ORDER BY s.updated_at DESC, s.id
        LIMIT @limit
        """;

    private static readonly string ListByRepositorySql =
        $"""
        {SessionSummarySelectSql}
        WHERE s.repository = @repository
        GROUP BY s.id
        ORDER BY s.updated_at DESC, s.id
        """;

    private static readonly string ListByCwdSql =
        $"""
        {SessionSummarySelectSql}
        WHERE s.cwd = @cwd
        GROUP BY s.id
        ORDER BY s.updated_at DESC, s.id
        """;

    private static readonly string GetByIdSql =
        $"""
        {SessionSummarySelectSql}
        WHERE s.id = @sessionId
        GROUP BY s.id
        """;

    private static readonly string GetTurnsSql =
        """
        SELECT id, session_id, turn_index, user_message, assistant_response, timestamp
        FROM turns
        WHERE session_id = @sessionId
        ORDER BY turn_index
        LIMIT @limit OFFSET @offset
        """;

    private static readonly string GetCheckpointsSql =
        """
        SELECT id, session_id, checkpoint_number, title, overview, history, work_done,
               technical_details, important_files, next_steps, created_at
        FROM checkpoints
        WHERE session_id = @sessionId
        ORDER BY checkpoint_number
        """;

    private static readonly string GetFilesSql =
        """
        SELECT id, session_id, file_path, tool_name, turn_index, first_seen_at
        FROM session_files
        WHERE session_id = @sessionId
        """;

    private static readonly string GetRefsSql =
        """
        SELECT id, session_id, ref_type, ref_value, turn_index, created_at
        FROM session_refs
        WHERE session_id = @sessionId
        """;

    // A session can have many matching rows (e.g. one per turn). Ranking and limiting
    // the raw FTS rows directly would let a handful of chatty sessions consume the whole
    // limit and crowd out every other matching session. Rank per session first (each
    // session contributes only its single best-matching row), then limit the number of
    // distinct sessions, so @limit means "top N sessions" rather than "top N raw hits".
    private static readonly string SearchSql =
        """
        WITH ranked AS (
            SELECT session_id, source_type, source_id, content, rank,
                   ROW_NUMBER() OVER (
                       PARTITION BY session_id
                       ORDER BY rank, search_index.rowid) AS rn
            FROM search_index
            WHERE search_index MATCH @query
        )
        SELECT session_id, source_type, source_id, content, rank
        FROM ranked
        WHERE rn = 1
        ORDER BY rank, session_id
        LIMIT @limit
        """;

    private static readonly string GlobalStatsSql =
        """
        SELECT
            (SELECT COUNT(*) FROM sessions) as total_sessions,
            (SELECT COUNT(*) FROM turns) as total_turns,
            (SELECT COUNT(*) FROM session_files) as total_files,
            (SELECT repository FROM sessions WHERE repository IS NOT NULL
             GROUP BY repository ORDER BY COUNT(*) DESC LIMIT 1) as most_active_repo,
            (SELECT DATE(created_at) FROM sessions
             GROUP BY DATE(created_at) ORDER BY COUNT(*) DESC LIMIT 1) as busiest_day
        """;

    private static readonly string ActivityTimelineSql =
        """
        WITH daily_activity AS (
            SELECT DATE(s.created_at) as day,
                   COUNT(*) as session_count,
                   0 as turn_count,
                   0 as file_count,
                   0 as checkpoint_count
            FROM sessions s
            WHERE DATE(s.created_at) >= DATE('now', @offset)
            GROUP BY day

            UNION ALL

            SELECT DATE(COALESCE(t.timestamp, s.created_at)) as day,
                   0 as session_count,
                   COUNT(*) as turn_count,
                   0 as file_count,
                   0 as checkpoint_count
            FROM turns t
            LEFT JOIN sessions s ON s.id = t.session_id
            WHERE DATE(COALESCE(t.timestamp, s.created_at)) >= DATE('now', @offset)
            GROUP BY day

            UNION ALL

            SELECT DATE(COALESCE(sf.first_seen_at, s.created_at)) as day,
                   0 as session_count,
                   0 as turn_count,
                   COUNT(*) as file_count,
                   0 as checkpoint_count
            FROM session_files sf
            LEFT JOIN sessions s ON s.id = sf.session_id
            WHERE DATE(COALESCE(sf.first_seen_at, s.created_at)) >= DATE('now', @offset)
            GROUP BY day

            UNION ALL

            SELECT DATE(COALESCE(c.created_at, s.created_at)) as day,
                   0 as session_count,
                   0 as turn_count,
                   0 as file_count,
                   COUNT(*) as checkpoint_count
            FROM checkpoints c
            LEFT JOIN sessions s ON s.id = c.session_id
            WHERE DATE(COALESCE(c.created_at, s.created_at)) >= DATE('now', @offset)
            GROUP BY day
        )
        SELECT day,
               SUM(session_count) as session_count,
               SUM(turn_count) as turn_count,
               SUM(file_count) as file_count,
               SUM(checkpoint_count) as checkpoint_count
        FROM daily_activity
        GROUP BY day
        ORDER BY day
        """;

    private static readonly string RepositoryStatsSql =
        """
        SELECT s.repository,
               COUNT(DISTINCT s.id) as session_count,
               COUNT(DISTINCT t.id) as turn_count,
               COUNT(DISTINCT sf.id) as file_count,
               MAX(s.updated_at) as last_activity
        FROM sessions s
        LEFT JOIN turns t ON t.session_id = s.id
        LEFT JOIN session_files sf ON sf.session_id = s.id
        WHERE s.repository IS NOT NULL
        GROUP BY s.repository
        ORDER BY session_count DESC
        """;

    private static readonly string SessionFileCountsSql =
        """
        SELECT session_id, COUNT(*)
        FROM session_files
        GROUP BY session_id
        """;

    // session_refs is intentionally not aggregated here (e.g. commit/PR/issue counts).
    // It is populated for only a small fraction of sessions and its values are not always
    // even well-formed (a 'commit' ref value has been observed to be a branch name), so it
    // is not a reliable outcomes signal — surfacing counts from it would understate real
    // activity by roughly an order of magnitude while looking authoritative.
    private static readonly string SessionInsightsSql =
        """
        SELECT
            (SELECT COUNT(DISTINCT repository) FROM sessions WHERE repository IS NOT NULL) as distinct_repos,
            (SELECT COUNT(DISTINCT branch) FROM sessions WHERE branch IS NOT NULL) as distinct_branches,
            (SELECT COUNT(*) FROM checkpoints) as total_checkpoints,
            (SELECT COUNT(*) FROM session_files WHERE tool_name = 'create') as files_created,
            (SELECT COUNT(*) FROM session_files WHERE tool_name = 'edit') as files_edited,
            (SELECT COUNT(*) FROM sessions WHERE host_type = 'github') as github_sessions,
            (SELECT COUNT(*) FROM sessions WHERE host_type IS NULL OR host_type != 'github') as local_sessions
        """;

    private static readonly string SessionCreatedAtSql =
        """
        SELECT created_at FROM sessions WHERE created_at IS NOT NULL
        """;

    private static readonly string FileSummariesSql =
        """
        WITH file_summaries AS (
            SELECT sf.file_path,
                   COUNT(DISTINCT sf.session_id) as session_count,
                   MIN(sf.first_seen_at) as first_seen_at,
                   MAX(sf.first_seen_at) as last_seen_at,
                   CASE
                       WHEN @query = '' THEN 0
                       WHEN narnia_path_equals(sf.file_path, @query) = 1 THEN 0
                       WHEN narnia_path_ends_with(sf.file_path, @query) = 1 THEN 1
                       ELSE 2
                   END as match_rank,
                   (
                       SELECT sf2.tool_name
                       FROM session_files sf2
                       WHERE sf2.file_path = sf.file_path
                       ORDER BY sf2.first_seen_at DESC, sf2.id DESC
                       LIMIT 1
                   ) as last_tool_name
            FROM session_files sf
            WHERE sf.file_path IS NOT NULL
              AND (
                  @query = ''
                  OR narnia_path_contains(sf.file_path, @query) = 1
              )
            GROUP BY sf.file_path
        )
        SELECT file_path, session_count, last_tool_name, first_seen_at, last_seen_at
        FROM file_summaries
        ORDER BY
            CASE WHEN @popular = 1 THEN session_count END DESC,
            CASE WHEN @popular = 0 THEN match_rank END,
            last_seen_at DESC,
            session_count DESC,
            file_path COLLATE NOCASE
        LIMIT @limit
        """;

    private static readonly string FileHistorySql =
        """
        WITH file_matches AS (
            SELECT sf.session_id,
                   sf.file_path,
                   sf.tool_name,
                   sf.first_seen_at,
                   ROW_NUMBER() OVER (
                       PARTITION BY sf.session_id
                       ORDER BY sf.first_seen_at IS NULL, sf.first_seen_at DESC, sf.id DESC
                   ) as rn
            FROM session_files sf
            WHERE narnia_path_equals(sf.file_path, @filePath) = 1
        )
        SELECT fm.session_id,
               s.summary,
               fm.file_path,
               fm.tool_name,
               fm.first_seen_at,
               (
                   SELECT c.overview
                   FROM checkpoints c
                   WHERE c.session_id = fm.session_id
                   ORDER BY c.checkpoint_number DESC
                   LIMIT 1
               ) as checkpoint_overview
        FROM file_matches fm
        JOIN sessions s ON s.id = fm.session_id
        WHERE fm.rn = 1
        ORDER BY fm.first_seen_at IS NULL, fm.first_seen_at DESC, fm.session_id
        """;

    // Tags each matching session with whether it came from an explicit session_refs row
    // (is_confirmed = 1) or only from a text mention picked up by the FTS fallback
    // (is_confirmed = 0), so the caller can show a confidence signal instead of presenting
    // every match as equally authoritative. A session can appear in more than one arm of the
    // UNION (e.g. both an explicit ref and a text mention); MAX(...) after GROUP BY picks the
    // higher-confidence tag when that happens.
    private static readonly string SessionsByRefSql =
        """
        SELECT s.id, s.cwd, s.repository, s.branch, s.summary, s.created_at, s.updated_at,
               COUNT(DISTINCT t.id) as turn_count, COUNT(DISTINCT c.id) as checkpoint_count,
               MAX(m.is_confirmed) as is_confirmed
        FROM sessions s
        JOIN (
            SELECT sr.session_id, 1 as is_confirmed
            FROM session_refs sr
            WHERE sr.ref_value = @refValue
               OR sr.ref_value LIKE @refPrefix
               OR @refValue LIKE sr.ref_value || '%'
            UNION
            SELECT si.session_id, 0 as is_confirmed
            FROM search_index si
            WHERE si.search_index MATCH @ftsQuery
            UNION
            SELECT si.session_id, 0 as is_confirmed
            FROM search_index si
            WHERE si.search_index MATCH @ftsShortQuery
        ) m ON m.session_id = s.id
        LEFT JOIN turns t ON t.session_id = s.id
        LEFT JOIN checkpoints c ON c.session_id = s.id
        GROUP BY s.id
        ORDER BY is_confirmed DESC, s.updated_at DESC, s.id
        """;

    private static readonly string ResumeSuggestionsSql =
        """
        WITH latest_checkpoints AS (
            SELECT session_id,
                   title,
                   next_steps,
                   ROW_NUMBER() OVER (
                       PARTITION BY session_id
                       ORDER BY checkpoint_number DESC) as row_number
            FROM checkpoints
        ),
        turn_counts AS (
            SELECT session_id, COUNT(*) as turn_count
            FROM turns
            GROUP BY session_id
        ),
        checkpoint_counts AS (
            SELECT session_id, COUNT(*) as checkpoint_count
            FROM checkpoints
            GROUP BY session_id
        )
        SELECT s.id, s.cwd, s.repository, s.branch, s.summary, s.created_at, s.updated_at,
               COALESCE(t.turn_count, 0) as turn_count,
               COALESCE(cc.checkpoint_count, 0) as checkpoint_count,
               c.title as cp_title,
               c.next_steps
        FROM sessions s
        JOIN latest_checkpoints c ON c.session_id = s.id AND c.row_number = 1
        LEFT JOIN turn_counts t ON t.session_id = s.id
        LEFT JOIN checkpoint_counts cc ON cc.session_id = s.id
        WHERE c.next_steps IS NOT NULL AND trim(c.next_steps) != ''
        ORDER BY s.updated_at DESC, s.id
        LIMIT @limit
        """;

    private static readonly string TopKeywordsSql =
        """
        SELECT summary FROM sessions WHERE summary IS NOT NULL
        UNION ALL
        SELECT overview FROM checkpoints WHERE overview IS NOT NULL
        """;

    /// <inheritdoc />
    public ValueTask<SessionSummary[]> ListAllAsync(bool includeArchived = false, CancellationToken ct = default)
    {
        _ = includeArchived;
        return QuerySessionSummariesAsync(ListAllSql, configure: null, ct);
    }

    public ValueTask<SessionSummary[]> ListRecentAsync(int limit = 20, bool includeArchived = false, CancellationToken ct = default)
    {
        _ = includeArchived;
        return QuerySessionSummariesAsync(
            ListRecentSql,
            cmd => cmd.Parameters.AddWithValue("@limit", limit),
            ct);
    }

    public ValueTask<SessionSummary[]> ListByRepositoryAsync(string repository, bool includeArchived = false, CancellationToken ct = default)
    {
        _ = includeArchived;
        return QuerySessionSummariesAsync(
            ListByRepositorySql,
            cmd => cmd.Parameters.AddWithValue("@repository", repository),
            ct);
    }

    public ValueTask<SessionSummary[]> ListByCwdAsync(string cwd, bool includeArchived = false, CancellationToken ct = default)
    {
        _ = includeArchived;
        return QuerySessionSummariesAsync(
            ListByCwdSql,
            cmd => cmd.Parameters.AddWithValue("@cwd", cwd),
            ct);
    }

    public async ValueTask<Session?> GetByIdAsync(string sessionId, CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = GetByIdSql;
        cmd.Parameters.AddWithValue("@sessionId", sessionId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;

        return ReadSession(reader);
    }

    public async ValueTask<Turn[]> GetTurnsAsync(string sessionId, int offset = 0, int limit = 50, CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = GetTurnsSql;
        cmd.Parameters.AddWithValue("@sessionId", sessionId);
        cmd.Parameters.AddWithValue("@limit", limit);
        cmd.Parameters.AddWithValue("@offset", offset);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<Turn>();
        while (await reader.ReadAsync(ct))
            results.Add(ReadTurn(reader));
        return [.. results];
    }

    public async ValueTask<Checkpoint[]> GetCheckpointsAsync(string sessionId, CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = GetCheckpointsSql;
        cmd.Parameters.AddWithValue("@sessionId", sessionId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<Checkpoint>();
        while (await reader.ReadAsync(ct))
            results.Add(ReadCheckpoint(reader));
        return [.. results];
    }

    public async ValueTask<SessionFile[]> GetFilesAsync(string sessionId, CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = GetFilesSql;
        cmd.Parameters.AddWithValue("@sessionId", sessionId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<SessionFile>();
        while (await reader.ReadAsync(ct))
            results.Add(ReadSessionFile(reader));
        return [.. results];
    }

    public async ValueTask<SessionRef[]> GetRefsAsync(string sessionId, CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = GetRefsSql;
        cmd.Parameters.AddWithValue("@sessionId", sessionId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<SessionRef>();
        while (await reader.ReadAsync(ct))
            results.Add(ReadSessionRef(reader));
        return [.. results];
    }

    public ValueTask<SearchResult[]> SearchAsync(
        string query,
        int limit = 20,
        CancellationToken ct = default) =>
        SearchAsync(query, limit, includeArchived: false, ct);

    public async ValueTask<SearchResult[]> SearchAsync(
        string query,
        int limit,
        bool includeArchived,
        CancellationToken ct = default)
    {
        _ = includeArchived;
        var sanitizedQuery = SanitizeFts5Query(query);
        if (sanitizedQuery.Length == 0)
            return [];

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = SearchSql;
        cmd.Parameters.AddWithValue("@query", sanitizedQuery);
        cmd.Parameters.AddWithValue("@limit", limit);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<SearchResult>();
        while (await reader.ReadAsync(ct))
            results.Add(ReadSearchResult(reader));
        return [.. results];
    }

    internal async ValueTask<IReadOnlyDictionary<string, int>> GetFileCountsBySessionAsync(CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = SessionFileCountsSql;

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new Dictionary<string, int>(StringComparer.Ordinal);
        while (await reader.ReadAsync(ct))
            results[reader.GetString(0)] = reader.GetInt32(1);
        return results;
    }

    public async ValueTask<GlobalStats> GetGlobalStatsAsync(CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = GlobalStatsSql;

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return new GlobalStats(0, 0, 0, 0, null, null);

        var totalSessions = reader.GetInt32(0);
        var totalTurns = reader.GetInt32(1);
        var totalFiles = reader.GetInt32(2);
        var mostActiveRepo = reader.IsDBNull(3) ? null : reader.GetString(3);
        var busiestDay = reader.IsDBNull(4) ? null : reader.GetString(4);
        var avg = totalSessions > 0 ? Math.Round((double)totalTurns / totalSessions, 1) : 0;

        return new GlobalStats(totalSessions, totalTurns, avg, totalFiles, mostActiveRepo, busiestDay);
    }

    /// <inheritdoc />
    public async ValueTask<ActivityDay[]> GetActivityByDateAsync(int days = 90, CancellationToken ct = default)
    {
        var timeline = await GetActivityTimelineAsync(days, ct);
        return [.. timeline
            .Where(day => day.SessionCount > 0)
            .Select(day => new ActivityDay(day.Date, day.SessionCount))];
    }

    /// <inheritdoc />
    public async ValueTask<ActivityTimelineDay[]> GetActivityTimelineAsync(
        int days = 90,
        CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = ActivityTimelineSql;
        cmd.Parameters.AddWithValue("@offset", $"-{days} days");

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<ActivityTimelineDay>();
        while (await reader.ReadAsync(ct))
        {
            results.Add(new ActivityTimelineDay(
                Date: DateOnly.Parse(reader.GetString(0)),
                SessionCount: reader.GetInt32(1),
                TurnCount: reader.GetInt32(2),
                FilesTouched: reader.GetInt32(3),
                CheckpointCount: reader.GetInt32(4)));
        }
        return [.. results];
    }

    public async ValueTask<RepositoryStats[]> GetRepositoryStatsAsync(CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = RepositoryStatsSql;

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<RepositoryStats>();
        while (await reader.ReadAsync(ct))
        {
            var repo = reader.GetString(0);
            var sessions = reader.GetInt32(1);
            var turns = reader.GetInt32(2);
            var files = reader.GetInt32(3);
            var lastActivity = ParseDateTimeOffset(reader.IsDBNull(4) ? null : reader.GetString(4));
            results.Add(new RepositoryStats(repo, sessions, turns, files, lastActivity));
        }
        return [.. results];
    }

    public async ValueTask<SessionInsights> GetSessionInsightsAsync(CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = SessionInsightsSql;

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return new SessionInsights(0, 0, 0, 0, 0, 0, 0);

        return new SessionInsights(
            DistinctRepositories: reader.GetInt32(0),
            DistinctBranches: reader.GetInt32(1),
            TotalCheckpoints: reader.GetInt32(2),
            FilesCreated: reader.GetInt32(3),
            FilesEdited: reader.GetInt32(4),
            GithubHostedSessions: reader.GetInt32(5),
            LocalTerminalSessions: reader.GetInt32(6));
    }

    public async ValueTask<ActivityPatterns> GetActivityPatternsAsync(CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = SessionCreatedAtSql;

        var byHour = new int[24];
        var byDayOfWeek = new int[7];
        var localDates = new List<DateOnly>();

        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                if (reader.IsDBNull(0) || !DateTimeOffset.TryParse(reader.GetString(0), out var utc))
                    continue;

                // created_at is stored in UTC; convert so "peak hours" and streaks line
                // up with the machine's own clock instead of the database's UTC values.
                var local = utc.ToLocalTime();
                byHour[local.Hour]++;
                byDayOfWeek[(int)local.DayOfWeek]++;
                localDates.Add(DateOnly.FromDateTime(local.Date));
            }
        }

        var hourResults = Enumerable.Range(0, 24)
            .Select(h => new HourActivity(h, byHour[h]))
            .ToArray();
        var dayResults = Enum.GetValues<DayOfWeek>()
            .Select(d => new DayOfWeekActivity(d, byDayOfWeek[(int)d]))
            .ToArray();
        var (current, longest) = ComputeStreaks(localDates);

        return new ActivityPatterns(hourResults, dayResults, current, longest);
    }

    private static (int Current, int Longest) ComputeStreaks(List<DateOnly> activeDates)
    {
        if (activeDates.Count == 0)
            return (0, 0);

        var distinctDays = activeDates.Distinct().OrderBy(d => d).ToArray();

        var longest = 1;
        var run = 1;
        for (var i = 1; i < distinctDays.Length; i++)
        {
            run = distinctDays[i].DayNumber == distinctDays[i - 1].DayNumber + 1 ? run + 1 : 1;
            longest = Math.Max(longest, run);
        }

        var today = DateOnly.FromDateTime(DateTime.Now.Date);
        var mostRecent = distinctDays[^1];
        if (mostRecent != today && mostRecent != today.AddDays(-1))
            return (0, longest);

        var current = 1;
        for (var i = distinctDays.Length - 1; i > 0; i--)
        {
            if (distinctDays[i].DayNumber != distinctDays[i - 1].DayNumber + 1)
                break;
            current++;
        }

        return (current, longest);
    }

    public async ValueTask<HotFile[]> GetHotFilesAsync(int limit = 20, CancellationToken ct = default)
        => await QueryFileSummariesAsync(query: "", limit, popular: true, ct);

    public async ValueTask<HotFile[]> SearchFilesAsync(string query, int limit = 100, CancellationToken ct = default)
        => await QueryFileSummariesAsync(query, limit, popular: false, ct);

    private async ValueTask<HotFile[]> QueryFileSummariesAsync(
        string query,
        int limit,
        bool popular,
        CancellationToken ct)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);
        RegisterFilePathFunctions(connection);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = FileSummariesSql;
        cmd.Parameters.AddWithValue("@query", query);
        cmd.Parameters.AddWithValue("@popular", popular ? 1 : 0);
        cmd.Parameters.AddWithValue("@limit", limit);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<HotFile>();
        while (await reader.ReadAsync(ct))
        {
            var filePath = reader.GetString(0);
            var sessionCount = reader.GetInt32(1);
            var toolName = reader.IsDBNull(2) ? null : reader.GetString(2);
            var firstSeenAt = ParseNullableDateTimeOffset(reader.IsDBNull(3) ? null : reader.GetString(3));
            var lastSeenAt = ParseNullableDateTimeOffset(reader.IsDBNull(4) ? null : reader.GetString(4));
            results.Add(new HotFile(filePath, sessionCount, toolName)
            {
                FirstSeenAt = firstSeenAt,
                LastSeenAt = lastSeenAt,
            });
        }
        return [.. results];
    }

    public async ValueTask<FileHistoryEntry[]> GetFileHistoryAsync(string filePath, CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);
        RegisterFilePathFunctions(connection);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = FileHistorySql;
        cmd.Parameters.AddWithValue("@filePath", filePath);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<FileHistoryEntry>();
        while (await reader.ReadAsync(ct))
        {
            var sessionId = reader.GetString(0);
            var summary = reader.IsDBNull(1) ? null : reader.GetString(1);
            var recordedPath = reader.IsDBNull(2) ? null : reader.GetString(2);
            var toolName = reader.IsDBNull(3) ? null : reader.GetString(3);
            var firstSeenAt = ParseNullableDateTimeOffset(reader.IsDBNull(4) ? null : reader.GetString(4));
            var overview = reader.IsDBNull(5) ? null : reader.GetString(5);
            results.Add(new FileHistoryEntry(sessionId, summary, toolName, firstSeenAt, overview)
            {
                RecordedPath = recordedPath,
            });
        }
        return [.. results];
    }

    public async ValueTask<CommitMatch[]> GetSessionsByRefAsync(string refValue, CancellationToken ct = default)
    {
        // Reject anything that isn't a plausible SHA before it ever reaches the FTS5 MATCH
        // parameter below, which parses its argument as a small query language: an unvalidated
        // value (a stray quote, colon, "OR"/"NOT", or an empty/1-character string) can throw a
        // syntax error or match a meaninglessly broad slice of every session's content.
        var query = CommitShaQuery.TryParse(refValue);
        if (query is null)
            return [];

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = SessionsByRefSql;
        cmd.Parameters.AddWithValue("@refValue", query.Value);
        cmd.Parameters.AddWithValue("@refPrefix", query.Value + "%");
        cmd.Parameters.AddWithValue("@ftsQuery", ToFtsPrefixQuery(query.Value));
        cmd.Parameters.AddWithValue("@ftsShortQuery", ToFtsPrefixQuery(query.Value[..Math.Min(8, query.Value.Length)]));

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<CommitMatch>();
        while (await reader.ReadAsync(ct))
        {
            var session = ReadSessionSummary(reader);
            var confidence = reader.GetInt32(9) == 1 ? CommitMatchConfidence.Confirmed : CommitMatchConfidence.Mentioned;
            results.Add(new CommitMatch(session, confidence));
        }
        return [.. results];
    }

    public async ValueTask<ResumeSuggestion[]> GetResumeSuggestionsAsync(int limit = 10, CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = ResumeSuggestionsSql;
        cmd.Parameters.AddWithValue("@limit", limit);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<ResumeSuggestion>();
        while (await reader.ReadAsync(ct))
        {
            var session = ReadSessionSummary(reader);
            var cpTitle = reader.IsDBNull(9) ? null : reader.GetString(9);
            var nextSteps = reader.IsDBNull(10) ? null : reader.GetString(10);
            var preview = nextSteps is { Length: > 200 } ? nextSteps[..200] + "…" : nextSteps;
            results.Add(new ResumeSuggestion(session, cpTitle, preview));
        }
        return [.. results];
    }

    public async ValueTask<Dictionary<string, string>> GetResumableSessionIdsAsync(IReadOnlyList<string> sessionIds, CancellationToken ct = default)
    {
        if (sessionIds.Count == 0)
            return [];

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);

        var paramNames = new string[sessionIds.Count];
        for (var i = 0; i < sessionIds.Count; i++)
            paramNames[i] = $"@id{i}";

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            SELECT s.id, c.next_steps
            FROM sessions s
            JOIN checkpoints c ON c.session_id = s.id
                AND c.checkpoint_number = (
                    SELECT MAX(checkpoint_number) FROM checkpoints WHERE session_id = s.id)
            WHERE c.next_steps IS NOT NULL AND trim(c.next_steps) != ''
            AND s.id IN ({string.Join(", ", paramNames)})
            """;

        for (var i = 0; i < sessionIds.Count; i++)
            cmd.Parameters.AddWithValue(paramNames[i], sessionIds[i]);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var result = new Dictionary<string, string>();
        while (await reader.ReadAsync(ct))
        {
            var id = reader.GetString(0);
            var nextSteps = reader.GetString(1);
            result[id] = nextSteps.Length > 200 ? nextSteps[..200] + "…" : nextSteps;
        }
        return result;
    }

    public async ValueTask<KeywordFrequency[]> GetTopKeywordsAsync(int topN = 50, CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = TopKeywordsSql;

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var freq = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync(ct))
        {
            var text = reader.IsDBNull(0) ? null : reader.GetString(0);
            if (text is null) continue;
            foreach (var word in TokenizeKeywords(text))
            {
                freq.TryGetValue(word, out var c);
                freq[word] = c + 1;
            }
        }

        return [.. freq
            .OrderByDescending(kv => kv.Value)
            .Take(topN)
            .Select(kv => new KeywordFrequency(kv.Key, kv.Value))];
    }

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "the", "and", "or", "but", "in", "on", "at", "to", "for", "of", "with",
        "by", "from", "up", "about", "into", "through", "during", "is", "are", "was", "were",
        "be", "been", "being", "have", "has", "had", "do", "does", "did", "will", "would",
        "could", "should", "may", "might", "shall", "can", "it", "its", "this", "that",
        "these", "those", "i", "me", "my", "we", "our", "you", "your", "he", "she", "they",
        "their", "all", "as", "if", "so", "no", "not", "more", "also", "than", "then", "when",
        "which", "who", "what", "how", "use", "used", "using", "new", "add", "added"
    };

    // Replace FTS5 special characters with spaces, preserving * for prefix
    // wildcard queries. This avoids "syntax error near '/'" style exceptions
    // while keeping multi-word and wildcard searches functional.
    private static string SanitizeFts5Query(string query)
    {
        var sb = new System.Text.StringBuilder(query.Length);
        foreach (var ch in query)
        {
            if (char.IsLetterOrDigit(ch) || ch == '*' || ch == ' ')
                sb.Append(ch);
            else
                sb.Append(' ');
        }
        return sb.ToString().Trim();
    }

    // FTS5 MATCH parses its argument as a small query language (quoted phrases, boolean
    // operators, column filters). Quoting the value turns it into a literal phrase-prefix
    // query, so a validated CommitShaQuery can never be misread as query syntax even if the
    // validation rules change later.
    private static string ToFtsPrefixQuery(string value) =>
        $"\"{value.Replace("\"", "\"\"")}\"*";

    private static IEnumerable<string> TokenizeKeywords(string text)
    {
        var words = text.Split([' ', '\t', '\n', '\r', '.', ',', ':', ';', '!', '?', '(', ')', '[', ']', '"', '\'', '-', '_', '/'], StringSplitOptions.RemoveEmptyEntries);
        foreach (var w in words)
        {
            var clean = w.Trim().ToLowerInvariant();
            if (clean.Length >= 3 && !StopWords.Contains(clean) && clean.All(c => char.IsLetter(c)))
                yield return clean;
        }
    }

    private static void RegisterFilePathFunctions(SqliteConnection connection)
    {
        connection.CreateFunction<string?, string?, bool>(
            "narnia_path_contains",
            FilePathContains,
            isDeterministic: true);
        connection.CreateFunction<string?, string?, bool>(
            "narnia_path_equals",
            FilePathsEqual,
            isDeterministic: true);
        connection.CreateFunction<string?, string?, bool>(
            "narnia_path_ends_with",
            FilePathEndsWith,
            isDeterministic: true);
    }

    private static bool FilePathContains(string? path, string? query)
    {
        var normalizedPath = NormalizeFilePathShape(path);
        var normalizedQuery = NormalizeFilePathShape(query);
        return normalizedPath is not null
            && normalizedQuery is not null
            && normalizedPath.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase);
    }

    private static bool FilePathsEqual(string? left, string? right) =>
        string.Equals(
            NormalizeFilePathShape(left),
            NormalizeFilePathShape(right),
            StringComparison.OrdinalIgnoreCase);

    private static bool FilePathEndsWith(string? path, string? query)
    {
        var normalizedPath = NormalizeFilePathShape(path);
        var normalizedQuery = NormalizeFilePathShape(query);
        return normalizedPath is not null
            && normalizedQuery is not null
            && normalizedPath.EndsWith("/" + normalizedQuery, StringComparison.OrdinalIgnoreCase);
    }

    private static string? NormalizeFilePathShape(string? path) =>
        path?.Trim().Replace('\\', '/');

    private async ValueTask<SessionSummary[]> QuerySessionSummariesAsync(
        string commandText,
        Action<SqliteCommand>? configure,
        CancellationToken ct)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = commandText;
        configure?.Invoke(cmd);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<SessionSummary>();
        while (await reader.ReadAsync(ct))
            results.Add(ReadSessionSummary(reader));
        return [.. results];
    }

    private static SessionSummary ReadSessionSummary(SqliteDataReader reader)
    {
        var id = reader.GetString(0);
        var cwd = reader.IsDBNull(1) ? null : reader.GetString(1);
        var repository = reader.IsDBNull(2) ? null : reader.GetString(2);
        var branch = reader.IsDBNull(3) ? null : reader.GetString(3);
        var summary = reader.IsDBNull(4) ? null : reader.GetString(4);
        var createdAt = ParseDateTimeOffset(reader.IsDBNull(5) ? null : reader.GetString(5));
        var updatedAt = ParseDateTimeOffset(reader.IsDBNull(6) ? null : reader.GetString(6));
        var turnCount = reader.GetInt32(7);
        var checkpointCount = reader.GetInt32(8);

        return new SessionSummary(id, cwd, repository, branch, summary, createdAt, updatedAt, turnCount, checkpointCount);
    }

    private static Session ReadSession(SqliteDataReader reader)
    {
        var id = reader.GetString(0);
        var cwd = reader.IsDBNull(1) ? null : reader.GetString(1);
        var repository = reader.IsDBNull(2) ? null : reader.GetString(2);
        var branch = reader.IsDBNull(3) ? null : reader.GetString(3);
        var summary = reader.IsDBNull(4) ? null : reader.GetString(4);
        var createdAt = ParseDateTimeOffset(reader.IsDBNull(5) ? null : reader.GetString(5));
        var updatedAt = ParseDateTimeOffset(reader.IsDBNull(6) ? null : reader.GetString(6));
        var turnCount = reader.GetInt32(7);
        var checkpointCount = reader.GetInt32(8);

        return new Session(id, cwd, repository, branch, summary, null, createdAt, updatedAt, turnCount, checkpointCount);
    }

    private static Turn ReadTurn(SqliteDataReader reader)
    {
        var id = reader.GetInt64(0);
        var sessionId = reader.GetString(1);
        var turnIndex = reader.GetInt32(2);
        var userMessage = reader.IsDBNull(3) ? null : reader.GetString(3);
        var assistantResponse = reader.IsDBNull(4) ? null : reader.GetString(4);
        var timestamp = ParseDateTimeOffset(reader.IsDBNull(5) ? null : reader.GetString(5));

        return new Turn(id, sessionId, turnIndex, userMessage, assistantResponse, timestamp);
    }

    private static Checkpoint ReadCheckpoint(SqliteDataReader reader)
    {
        var id = reader.GetInt64(0);
        var sessionId = reader.GetString(1);
        var checkpointNumber = reader.GetInt32(2);
        var title = reader.IsDBNull(3) ? null : reader.GetString(3);
        var overview = reader.IsDBNull(4) ? null : reader.GetString(4);
        var history = reader.IsDBNull(5) ? null : reader.GetString(5);
        var workDone = reader.IsDBNull(6) ? null : reader.GetString(6);
        var technicalDetails = reader.IsDBNull(7) ? null : reader.GetString(7);
        var importantFiles = reader.IsDBNull(8) ? null : reader.GetString(8);
        var nextSteps = reader.IsDBNull(9) ? null : reader.GetString(9);
        var createdAt = ParseDateTimeOffset(reader.IsDBNull(10) ? null : reader.GetString(10));

        return new Checkpoint(id, sessionId, checkpointNumber, title, overview, history, workDone, technicalDetails, importantFiles, nextSteps, createdAt);
    }

    private static SessionFile ReadSessionFile(SqliteDataReader reader)
    {
        var id = reader.GetInt64(0);
        var sessionId = reader.GetString(1);
        var filePath = reader.IsDBNull(2) ? null : reader.GetString(2);
        var toolName = reader.IsDBNull(3) ? null : reader.GetString(3);
        var turnIndex = reader.IsDBNull(4) ? (int?)null : reader.GetInt32(4);
        var firstSeenAt = ParseDateTimeOffset(reader.IsDBNull(5) ? null : reader.GetString(5));

        return new SessionFile(id, sessionId, filePath, toolName, turnIndex, firstSeenAt);
    }

    private static SessionRef ReadSessionRef(SqliteDataReader reader)
    {
        var id = reader.GetInt64(0);
        var sessionId = reader.GetString(1);
        var refType = reader.IsDBNull(2) ? null : reader.GetString(2);
        var refValue = reader.IsDBNull(3) ? null : reader.GetString(3);
        var turnIndex = reader.IsDBNull(4) ? (int?)null : reader.GetInt32(4);
        var createdAt = ParseDateTimeOffset(reader.IsDBNull(5) ? null : reader.GetString(5));

        return new SessionRef(id, sessionId, refType, refValue, turnIndex, createdAt);
    }

    private static SearchResult ReadSearchResult(SqliteDataReader reader)
    {
        var sessionId = reader.GetString(0);
        var sourceType = reader.IsDBNull(1) ? null : reader.GetString(1);
        var sourceId = reader.IsDBNull(2) ? null : reader.GetString(2);
        var content = reader.IsDBNull(3) ? null : reader.GetString(3);
        var score = reader.GetDouble(4);

        return new SearchResult(sessionId, sourceType, sourceId, content, score);
    }

    private static DateTimeOffset ParseDateTimeOffset(string? value) =>
        DateTimeOffset.TryParse(value, out var dt) ? dt : DateTimeOffset.MinValue;

    private static DateTimeOffset? ParseNullableDateTimeOffset(string? value) =>
        DateTimeOffset.TryParse(value, out var dt) ? dt : null;
}
