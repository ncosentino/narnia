using Microsoft.Data.Sqlite;
using NexusLabs.Narnia.Core.Configuration;
using NexusLabs.Narnia.Core.Models;

namespace NexusLabs.Narnia.Core.Repositories;

public sealed class SqliteSessionOverridesRepository(NarniaOptions options) : ISessionOverridesRepository
{
    private readonly string _connectionString = options.SettingsConnectionString
        ?? $"Data Source={options.SettingsDatabasePath}";

    public async ValueTask<SessionOverride?> GetOverrideAsync(string sessionId, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            SELECT session_id, display_name, repository, branch, notes, created_at, updated_at, is_archived, local_path, terminal_title, is_favorite
            FROM session_overrides
            WHERE session_id = @session_id
            """;
        cmd.Parameters.AddWithValue("@session_id", sessionId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;

        return ReadSessionOverride(reader);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyDictionary<string, SessionOverride>> GetAllOverridesAsync(CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            SELECT session_id, display_name, repository, branch, notes, created_at, updated_at, is_archived, local_path, terminal_title, is_favorite
            FROM session_overrides
            """;

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var result = new Dictionary<string, SessionOverride>(StringComparer.Ordinal);
        while (await reader.ReadAsync(ct))
        {
            var sessionOverride = ReadSessionOverride(reader);
            result[sessionOverride.SessionId] = sessionOverride;
        }

        return result;
    }

    /// <inheritdoc />
    public async ValueTask UpsertMetadataAsync(SessionOverride sessionOverride, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            INSERT INTO session_overrides (session_id, display_name, repository, branch, notes, created_at, updated_at, local_path, terminal_title)
            VALUES (@session_id, @display_name, @repository, @branch, @notes, @created_at, @updated_at, @local_path, @terminal_title)
            ON CONFLICT(session_id) DO UPDATE SET
                display_name = excluded.display_name,
                repository   = excluded.repository,
                branch       = excluded.branch,
                notes        = excluded.notes,
                updated_at   = excluded.updated_at,
                local_path   = excluded.local_path,
                terminal_title = excluded.terminal_title
            """;
        cmd.Parameters.AddWithValue("@session_id", sessionOverride.SessionId);
        cmd.Parameters.AddWithValue("@display_name", (object?)sessionOverride.DisplayName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@repository", (object?)sessionOverride.Repository ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@branch", (object?)sessionOverride.Branch ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@notes", (object?)sessionOverride.Notes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@created_at", sessionOverride.CreatedAt.ToString("o"));
        cmd.Parameters.AddWithValue("@updated_at", sessionOverride.UpdatedAt.ToString("o"));
        cmd.Parameters.AddWithValue("@local_path", (object?)sessionOverride.LocalPath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@terminal_title", (object?)sessionOverride.TerminalTitle ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <inheritdoc />
    public async ValueTask ResetMetadataAsync(
        string sessionId,
        DateTimeOffset updatedAt,
        CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var transaction = await conn.BeginTransactionAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.Transaction = (SqliteTransaction)transaction;
        cmd.CommandText =
            """
            UPDATE session_overrides
            SET display_name = NULL,
                repository = NULL,
                branch = NULL,
                notes = NULL,
                local_path = NULL,
                terminal_title = NULL,
                updated_at = @updated_at
            WHERE session_id = @session_id;

            DELETE FROM session_overrides
            WHERE session_id = @session_id
              AND is_archived = 0
              AND is_favorite = 0;
            """;
        cmd.Parameters.AddWithValue("@session_id", sessionId);
        cmd.Parameters.AddWithValue("@updated_at", updatedAt.ToString("o"));

        await cmd.ExecuteNonQueryAsync(ct);
        await transaction.CommitAsync(ct);
    }

    /// <inheritdoc />
    public ValueTask SetArchivedAsync(
        string sessionId,
        bool isArchived,
        DateTimeOffset updatedAt,
        CancellationToken ct = default) =>
        SetFlagAsync(sessionId, "is_archived", isArchived, updatedAt, ct);

    /// <inheritdoc />
    public ValueTask SetFavoriteAsync(
        string sessionId,
        bool isFavorite,
        DateTimeOffset updatedAt,
        CancellationToken ct = default) =>
        SetFlagAsync(sessionId, "is_favorite", isFavorite, updatedAt, ct);

    private async ValueTask SetFlagAsync(
        string sessionId,
        string column,
        bool value,
        DateTimeOffset updatedAt,
        CancellationToken ct)
    {
        if (column is not ("is_archived" or "is_favorite"))
            throw new ArgumentOutOfRangeException(nameof(column), column, "Unsupported session override flag.");

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var transaction = await conn.BeginTransactionAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.Transaction = (SqliteTransaction)transaction;
        cmd.CommandText =
            $"""
            INSERT INTO session_overrides (session_id, created_at, updated_at, {column})
            VALUES (@session_id, @updated_at, @updated_at, @value)
            ON CONFLICT(session_id) DO UPDATE SET
                updated_at = excluded.updated_at,
                {column} = excluded.{column};

            DELETE FROM session_overrides
            WHERE session_id = @session_id
              AND is_archived = 0
              AND is_favorite = 0
              AND display_name IS NULL
              AND repository IS NULL
              AND branch IS NULL
              AND notes IS NULL
              AND local_path IS NULL
              AND terminal_title IS NULL;
            """;
        cmd.Parameters.AddWithValue("@session_id", sessionId);
        cmd.Parameters.AddWithValue("@updated_at", updatedAt.ToString("o"));
        cmd.Parameters.AddWithValue("@value", value ? 1 : 0);

        await cmd.ExecuteNonQueryAsync(ct);
        await transaction.CommitAsync(ct);
    }

    public async ValueTask<HashSet<string>> GetArchivedSessionIdsAsync(CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT session_id FROM session_overrides WHERE is_archived = 1";

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var result = new HashSet<string>(StringComparer.Ordinal);
        while (await reader.ReadAsync(ct))
            result.Add(reader.GetString(0));
        return result;
    }

    private static SessionOverride ReadSessionOverride(SqliteDataReader reader)
    {
        var sessionId = reader.GetString(0);
        var displayName = reader.IsDBNull(1) ? null : reader.GetString(1);
        var repository = reader.IsDBNull(2) ? null : reader.GetString(2);
        var branch = reader.IsDBNull(3) ? null : reader.GetString(3);
        var notes = reader.IsDBNull(4) ? null : reader.GetString(4);
        var createdAt = ParseDateTimeOffset(reader.IsDBNull(5) ? null : reader.GetString(5));
        var updatedAt = ParseDateTimeOffset(reader.IsDBNull(6) ? null : reader.GetString(6));
        var isArchived = !reader.IsDBNull(7) && reader.GetInt64(7) != 0;
        var localPath = reader.FieldCount > 8 && !reader.IsDBNull(8) ? reader.GetString(8) : null;
        var terminalTitle = reader.FieldCount > 9 && !reader.IsDBNull(9) ? reader.GetString(9) : null;
        var isFavorite = reader.FieldCount > 10 && !reader.IsDBNull(10) && reader.GetInt64(10) != 0;

        return new SessionOverride(sessionId, displayName, repository, branch, notes, createdAt, updatedAt)
        {
            IsArchived = isArchived,
            IsFavorite = isFavorite,
            LocalPath = localPath,
            TerminalTitle = terminalTitle,
        };
    }

    private static DateTimeOffset ParseDateTimeOffset(string? value) =>
        DateTimeOffset.TryParse(value, out var dt) ? dt : DateTimeOffset.MinValue;
}
