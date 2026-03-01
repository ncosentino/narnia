using Microsoft.Data.Sqlite;
using NexusLabs.Narnia.Core.Configuration;
using NexusLabs.Narnia.Core.Models;

namespace NexusLabs.Narnia.Core.Repositories;

public sealed class SqliteSessionRepository(NarniaOptions options) : ISessionRepository, ISessionSearch
{
    private readonly string _connectionString = options.ConnectionString
        ?? $"Data Source={options.DatabasePath};Mode=ReadOnly";

    private static readonly string ListRecentSql =
        """
        SELECT s.id, s.cwd, s.repository, s.branch, s.summary, s.created_at, s.updated_at,
               COUNT(DISTINCT t.id) as turn_count, COUNT(DISTINCT c.id) as checkpoint_count
        FROM sessions s
        LEFT JOIN turns t ON t.session_id = s.id
        LEFT JOIN checkpoints c ON c.session_id = s.id
        GROUP BY s.id
        ORDER BY s.updated_at DESC
        LIMIT @limit
        """;

    private static readonly string ListByRepositorySql =
        """
        SELECT s.id, s.cwd, s.repository, s.branch, s.summary, s.created_at, s.updated_at,
               COUNT(DISTINCT t.id) as turn_count, COUNT(DISTINCT c.id) as checkpoint_count
        FROM sessions s
        LEFT JOIN turns t ON t.session_id = s.id
        LEFT JOIN checkpoints c ON c.session_id = s.id
        WHERE s.repository = @repository
        GROUP BY s.id
        ORDER BY s.updated_at DESC
        """;

    private static readonly string ListByCwdSql =
        """
        SELECT s.id, s.cwd, s.repository, s.branch, s.summary, s.created_at, s.updated_at,
               COUNT(DISTINCT t.id) as turn_count, COUNT(DISTINCT c.id) as checkpoint_count
        FROM sessions s
        LEFT JOIN turns t ON t.session_id = s.id
        LEFT JOIN checkpoints c ON c.session_id = s.id
        WHERE s.cwd = @cwd
        GROUP BY s.id
        ORDER BY s.updated_at DESC
        """;

    private static readonly string GetByIdSql =
        """
        SELECT s.id, s.cwd, s.repository, s.branch, s.summary, s.created_at, s.updated_at,
               COUNT(DISTINCT t.id) as turn_count, COUNT(DISTINCT c.id) as checkpoint_count
        FROM sessions s
        LEFT JOIN turns t ON t.session_id = s.id
        LEFT JOIN checkpoints c ON c.session_id = s.id
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

    private static readonly string SearchSql =
        """
        SELECT session_id, source_type, source_id, content, rank
        FROM search_index
        WHERE search_index MATCH @query
        ORDER BY rank
        LIMIT @limit
        """;

    public async ValueTask<SessionSummary[]> ListRecentAsync(int limit = 20, CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = ListRecentSql;
        cmd.Parameters.AddWithValue("@limit", limit);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<SessionSummary>();
        while (await reader.ReadAsync(ct))
            results.Add(ReadSessionSummary(reader));
        return [.. results];
    }

    public async ValueTask<SessionSummary[]> ListByRepositoryAsync(string repository, CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = ListByRepositorySql;
        cmd.Parameters.AddWithValue("@repository", repository);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<SessionSummary>();
        while (await reader.ReadAsync(ct))
            results.Add(ReadSessionSummary(reader));
        return [.. results];
    }

    public async ValueTask<SessionSummary[]> ListByCwdAsync(string cwd, CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = ListByCwdSql;
        cmd.Parameters.AddWithValue("@cwd", cwd);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<SessionSummary>();
        while (await reader.ReadAsync(ct))
            results.Add(ReadSessionSummary(reader));
        return [.. results];
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

    public async ValueTask<SearchResult[]> SearchAsync(string query, int limit = 20, CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = SearchSql;
        cmd.Parameters.AddWithValue("@query", query);
        cmd.Parameters.AddWithValue("@limit", limit);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<SearchResult>();
        while (await reader.ReadAsync(ct))
            results.Add(ReadSearchResult(reader));
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
}
