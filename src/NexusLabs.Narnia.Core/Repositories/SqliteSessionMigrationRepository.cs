using Microsoft.Data.Sqlite;
using NexusLabs.Narnia.Core.Configuration;
using NexusLabs.Narnia.Core.Models;
using NexusLabs.Narnia.Core.Services;

namespace NexusLabs.Narnia.Core.Repositories;

/// <summary>Stores migration provenance and atomically carries Narnia references forward.</summary>
public sealed class SqliteSessionMigrationRepository(NarniaOptions options)
    : ISessionMigrationRepository
{
    private const string MigrationColumns =
        """
        id, source_session_id, replacement_session_id, status, recovery_packet_path,
        recovery_packet_bytes, recovery_packet_truncated, archived_events_path,
        archived_events_sha256, baseline_turn_count, baseline_updated_at, error,
        created_at, updated_at, completed_at
        """;

    private readonly string _connectionString = options.SettingsConnectionString
        ?? $"Data Source={options.SettingsDatabasePath}";

    /// <inheritdoc />
    public ValueTask<SessionMigration?> GetLatestBySourceAsync(
        string sourceSessionId,
        CancellationToken ct) =>
        GetSingleAsync(
            $"SELECT {MigrationColumns} FROM session_migrations WHERE source_session_id = @value ORDER BY created_at DESC, id DESC LIMIT 1",
            sourceSessionId,
            ct);

    /// <inheritdoc />
    public ValueTask<SessionMigration?> GetByReplacementAsync(
        string replacementSessionId,
        CancellationToken ct) =>
        GetSingleAsync(
            $"SELECT {MigrationColumns} FROM session_migrations WHERE replacement_session_id = @value ORDER BY created_at DESC, id DESC LIMIT 1",
            replacementSessionId,
            ct);

    /// <inheritdoc />
    public ValueTask<SessionMigration?> GetByIdAsync(
        string migrationId,
        CancellationToken ct) =>
        GetSingleAsync(
            $"SELECT {MigrationColumns} FROM session_migrations WHERE id = @value LIMIT 1",
            migrationId,
            ct);

    /// <inheritdoc />
    public async ValueTask<SessionMigrationReferenceSummary> GetReferenceSummaryAsync(
        string sourceSessionId,
        CancellationToken ct)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                EXISTS(
                    SELECT 1 FROM session_overrides
                    WHERE session_id = @sessionId AND is_favorite = 1),
                EXISTS(
                    SELECT 1 FROM session_overrides
                    WHERE session_id = @sessionId
                      AND display_name IS NOT NULL
                      AND TRIM(display_name) <> ''),
                EXISTS(
                    SELECT 1 FROM session_overrides
                    WHERE session_id = @sessionId
                      AND notes IS NOT NULL
                      AND TRIM(notes) <> ''),
                (SELECT COUNT(*) FROM work_collection_sessions WHERE session_id = @sessionId),
                (SELECT COUNT(*) FROM session_group_members WHERE session_id = @sessionId),
                (SELECT COUNT(*) FROM terminal_window_tabs WHERE session_id = @sessionId)
            """;
        command.Parameters.AddWithValue("@sessionId", sourceSessionId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return new SessionMigrationReferenceSummary(false, false, false, 0, 0, 0);

        return new SessionMigrationReferenceSummary(
            reader.GetInt64(0) != 0,
            reader.GetInt64(1) != 0,
            reader.GetInt64(2) != 0,
            reader.GetInt32(3),
            reader.GetInt32(4),
            reader.GetInt32(5));
    }

    /// <inheritdoc />
    public async ValueTask AddAsync(SessionMigration migration, CancellationToken ct)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO session_migrations (
                id, source_session_id, replacement_session_id, status, recovery_packet_path,
                recovery_packet_bytes, recovery_packet_truncated, archived_events_path,
                archived_events_sha256, baseline_turn_count, baseline_updated_at, error,
                created_at, updated_at, completed_at)
            VALUES (
                @id, @source, @replacement, @status, @path, @bytes, @truncated,
                @archivedEventsPath, @archivedEventsSha256, @baselineTurnCount,
                @baselineUpdatedAt, @error, @createdAt, @updatedAt, @completedAt)
            """;
        AddMigrationParameters(command, migration);
        await command.ExecuteNonQueryAsync(ct);
    }

    /// <inheritdoc />
    public async ValueTask<bool> RestartAsync(
        SessionMigration migration,
        CancellationToken ct)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE session_migrations
            SET source_session_id = @source,
                replacement_session_id = @replacement,
                status = @status,
                recovery_packet_path = @path,
                recovery_packet_bytes = @bytes,
                recovery_packet_truncated = @truncated,
                archived_events_path = @archivedEventsPath,
                archived_events_sha256 = @archivedEventsSha256,
                baseline_turn_count = @baselineTurnCount,
                baseline_updated_at = @baselineUpdatedAt,
                error = NULL,
                created_at = @createdAt,
                updated_at = @updatedAt,
                completed_at = NULL
            WHERE id = @id
            """;
        AddMigrationParameters(command, migration);
        return await command.ExecuteNonQueryAsync(ct) > 0;
    }

    /// <inheritdoc />
    public ValueTask MarkSessionCreatedAsync(
        string migrationId,
        DateTimeOffset updatedAt,
        CancellationToken ct) =>
        UpdateStatusAsync(
            migrationId,
            SessionMigrationStatus.SessionCreated,
            null,
            updatedAt,
            null,
            ct);

    /// <inheritdoc />
    public ValueTask MarkCleanupRequiredAsync(
        string migrationId,
        string error,
        DateTimeOffset updatedAt,
        CancellationToken ct) =>
        UpdateStatusAsync(
            migrationId,
            SessionMigrationStatus.CleanupRequired,
            error,
            updatedAt,
            null,
            ct);

    /// <inheritdoc />
    public async ValueTask<bool> CompleteAsync(
        string migrationId,
        DateTimeOffset completedAt,
        CancellationToken ct)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var transaction =
            (SqliteTransaction)await connection.BeginTransactionAsync(ct);

        var migration = await GetByIdAsync(connection, transaction, migrationId, ct);
        if (migration is null)
        {
            await transaction.RollbackAsync(ct);
            return false;
        }

        if (!migration.IsInPlace)
        {
            var affectedWindowIds = await GetWindowIdsAsync(
                connection,
                transaction,
                migration.SourceSessionId,
                ct);
            await CloneOverrideAsync(connection, transaction, migration, completedAt, ct);
            await CopyCollectionMembershipsAsync(
                connection,
                transaction,
                migration,
                completedAt,
                ct);
            await ReplaceSessionGroupMembersAsync(
                connection,
                transaction,
                migration,
                completedAt,
                ct);
            await ReplaceWindowTabsAsync(connection, transaction, migration, ct);
            await RefreshCompositionKeysAsync(
                connection,
                transaction,
                affectedWindowIds,
                ct);
        }
        await UpdateStatusAsync(
            connection,
            transaction,
            migrationId,
            SessionMigrationStatus.Completed,
            null,
            completedAt,
            completedAt,
            ct);

        await transaction.CommitAsync(ct);
        return true;
    }

    /// <inheritdoc />
    public ValueTask MarkFailedAsync(
        string migrationId,
        string error,
        DateTimeOffset updatedAt,
        CancellationToken ct) =>
        UpdateStatusAsync(
            migrationId,
            SessionMigrationStatus.Failed,
            error,
            updatedAt,
            null,
            ct);

    /// <inheritdoc />
    public async ValueTask<bool> ResetAsync(
        string migrationId,
        string reason,
        DateTimeOffset updatedAt,
        CancellationToken ct)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var transaction =
            (SqliteTransaction)await connection.BeginTransactionAsync(ct);
        var migration = await GetByIdAsync(connection, transaction, migrationId, ct);
        if (migration is null)
        {
            await transaction.RollbackAsync(ct);
            return false;
        }

        if (!migration.IsInPlace)
        {
            await RemoveReplacementCollectionsAsync(
                connection,
                transaction,
                migration.ReplacementSessionId,
                updatedAt,
                ct);
            await RestoreSessionGroupMembersAsync(
                connection,
                transaction,
                migration,
                updatedAt,
                ct);
            var affectedWindowIds = await GetWindowIdsAsync(
                connection,
                transaction,
                migration.ReplacementSessionId,
                ct);
            await RestoreWindowTabsAsync(connection, transaction, migration, ct);
            await RefreshCompositionKeysAsync(
                connection,
                transaction,
                affectedWindowIds,
                ct);
            await DeleteReplacementOverrideAsync(
                connection,
                transaction,
                migration.ReplacementSessionId,
                ct);
        }
        await UpdateStatusAsync(
            connection,
            transaction,
            migrationId,
            SessionMigrationStatus.Failed,
            reason,
            updatedAt,
            null,
            ct);
        await transaction.CommitAsync(ct);
        return true;
    }

    /// <inheritdoc />
    public async ValueTask<HashSet<string>> GetRecoveryProtectedSessionIdsAsync(
        CancellationToken ct)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT source_session_id
            FROM session_migrations
            UNION
            SELECT replacement_session_id
            FROM session_migrations
            """;
        await using var reader = await command.ExecuteReaderAsync(ct);
        var sessionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync(ct))
            sessionIds.Add(reader.GetString(0));
        return sessionIds;
    }

    private async ValueTask<SessionMigration?> GetSingleAsync(
        string sql,
        string value,
        CancellationToken ct)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@value", value);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadMigration(reader) : null;
    }

    private static async ValueTask<SessionMigration?> GetByIdAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string migrationId,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"SELECT {MigrationColumns} FROM session_migrations WHERE id = @id LIMIT 1";
        command.Parameters.AddWithValue("@id", migrationId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadMigration(reader) : null;
    }

    private async ValueTask UpdateStatusAsync(
        string migrationId,
        SessionMigrationStatus status,
        string? error,
        DateTimeOffset updatedAt,
        DateTimeOffset? completedAt,
        CancellationToken ct)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE session_migrations
            SET status = @status,
                error = @error,
                updated_at = @updatedAt,
                completed_at = @completedAt
            WHERE id = @id
            """;
        AddStatusParameters(command, migrationId, status, error, updatedAt, completedAt);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async ValueTask UpdateStatusAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string migrationId,
        SessionMigrationStatus status,
        string? error,
        DateTimeOffset updatedAt,
        DateTimeOffset? completedAt,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            UPDATE session_migrations
            SET status = @status,
                error = @error,
                updated_at = @updatedAt,
                completed_at = @completedAt
            WHERE id = @id
            """;
        AddStatusParameters(command, migrationId, status, error, updatedAt, completedAt);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async ValueTask CloneOverrideAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SessionMigration migration,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO session_overrides (
                session_id, display_name, repository, branch, notes, created_at, updated_at,
                is_archived, local_path, terminal_title, is_favorite)
            SELECT
                @replacement, display_name, repository, branch, notes, @now, @now,
                0, local_path, terminal_title, is_favorite
            FROM session_overrides
            WHERE session_id = @source
            ON CONFLICT(session_id) DO UPDATE SET
                display_name = excluded.display_name,
                repository = excluded.repository,
                branch = excluded.branch,
                notes = excluded.notes,
                updated_at = excluded.updated_at,
                is_archived = 0,
                local_path = excluded.local_path,
                terminal_title = excluded.terminal_title,
                is_favorite = excluded.is_favorite
            """;
        command.Parameters.AddWithValue("@source", migration.SourceSessionId);
        command.Parameters.AddWithValue("@replacement", migration.ReplacementSessionId);
        command.Parameters.AddWithValue("@now", now.ToString("o"));
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async ValueTask CopyCollectionMembershipsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SessionMigration migration,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await using (var copy = connection.CreateCommand())
        {
            copy.Transaction = transaction;
            copy.CommandText =
                """
                INSERT OR IGNORE INTO work_collection_sessions (
                    collection_id, session_id, added_at)
                SELECT collection_id, @replacement, @now
                FROM work_collection_sessions
                WHERE session_id = @source
                """;
            copy.Parameters.AddWithValue("@source", migration.SourceSessionId);
            copy.Parameters.AddWithValue("@replacement", migration.ReplacementSessionId);
            copy.Parameters.AddWithValue("@now", now.ToString("o"));
            await copy.ExecuteNonQueryAsync(ct);
        }

        await using var touch = connection.CreateCommand();
        touch.Transaction = transaction;
        touch.CommandText =
            """
            UPDATE work_collections
            SET updated_at = @now
            WHERE id IN (
                SELECT collection_id
                FROM work_collection_sessions
                WHERE session_id = @replacement)
            """;
        touch.Parameters.AddWithValue("@replacement", migration.ReplacementSessionId);
        touch.Parameters.AddWithValue("@now", now.ToString("o"));
        await touch.ExecuteNonQueryAsync(ct);
    }

    private static async ValueTask RemoveReplacementCollectionsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string replacementSessionId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await using (var touch = connection.CreateCommand())
        {
            touch.Transaction = transaction;
            touch.CommandText =
                """
                UPDATE work_collections
                SET updated_at = @now
                WHERE id IN (
                    SELECT collection_id
                    FROM work_collection_sessions
                    WHERE session_id = @replacement)
                """;
            touch.Parameters.AddWithValue("@replacement", replacementSessionId);
            touch.Parameters.AddWithValue("@now", now.ToString("o"));
            await touch.ExecuteNonQueryAsync(ct);
        }

        await using var remove = connection.CreateCommand();
        remove.Transaction = transaction;
        remove.CommandText =
            "DELETE FROM work_collection_sessions WHERE session_id = @replacement";
        remove.Parameters.AddWithValue("@replacement", replacementSessionId);
        await remove.ExecuteNonQueryAsync(ct);
    }

    private static async ValueTask ReplaceSessionGroupMembersAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SessionMigration migration,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await using (var removeDuplicates = connection.CreateCommand())
        {
            removeDuplicates.Transaction = transaction;
            removeDuplicates.CommandText =
                """
                DELETE FROM session_group_members
                WHERE session_id = @source
                  AND group_id IN (
                      SELECT group_id
                      FROM session_group_members
                      WHERE session_id = @replacement)
                """;
            removeDuplicates.Parameters.AddWithValue("@source", migration.SourceSessionId);
            removeDuplicates.Parameters.AddWithValue(
                "@replacement",
                migration.ReplacementSessionId);
            await removeDuplicates.ExecuteNonQueryAsync(ct);
        }

        await using (var replace = connection.CreateCommand())
        {
            replace.Transaction = transaction;
            replace.CommandText =
                """
                UPDATE session_group_members
                SET session_id = @replacement
                WHERE session_id = @source
                """;
            replace.Parameters.AddWithValue("@source", migration.SourceSessionId);
            replace.Parameters.AddWithValue("@replacement", migration.ReplacementSessionId);
            await replace.ExecuteNonQueryAsync(ct);
        }

        await using var touch = connection.CreateCommand();
        touch.Transaction = transaction;
        touch.CommandText =
            """
            UPDATE session_groups
            SET updated_at = @now
            WHERE id IN (
                SELECT group_id
                FROM session_group_members
                WHERE session_id = @replacement)
            """;
        touch.Parameters.AddWithValue("@replacement", migration.ReplacementSessionId);
        touch.Parameters.AddWithValue("@now", now.ToString("o"));
        await touch.ExecuteNonQueryAsync(ct);
    }

    private static async ValueTask RestoreSessionGroupMembersAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SessionMigration migration,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await using (var removeDuplicates = connection.CreateCommand())
        {
            removeDuplicates.Transaction = transaction;
            removeDuplicates.CommandText =
                """
                DELETE FROM session_group_members
                WHERE session_id = @replacement
                  AND group_id IN (
                      SELECT group_id
                      FROM session_group_members
                      WHERE session_id = @source)
                """;
            removeDuplicates.Parameters.AddWithValue("@source", migration.SourceSessionId);
            removeDuplicates.Parameters.AddWithValue(
                "@replacement",
                migration.ReplacementSessionId);
            await removeDuplicates.ExecuteNonQueryAsync(ct);
        }

        await using (var restore = connection.CreateCommand())
        {
            restore.Transaction = transaction;
            restore.CommandText =
                """
                UPDATE session_group_members
                SET session_id = @source
                WHERE session_id = @replacement
                """;
            restore.Parameters.AddWithValue("@source", migration.SourceSessionId);
            restore.Parameters.AddWithValue("@replacement", migration.ReplacementSessionId);
            await restore.ExecuteNonQueryAsync(ct);
        }

        await using var touch = connection.CreateCommand();
        touch.Transaction = transaction;
        touch.CommandText =
            """
            UPDATE session_groups
            SET updated_at = @now
            WHERE id IN (
                SELECT group_id
                FROM session_group_members
                WHERE session_id = @source)
            """;
        touch.Parameters.AddWithValue("@source", migration.SourceSessionId);
        touch.Parameters.AddWithValue("@now", now.ToString("o"));
        await touch.ExecuteNonQueryAsync(ct);
    }

    private static async ValueTask<IReadOnlyList<string>> GetWindowIdsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sourceSessionId,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT window_id
            FROM terminal_window_tabs
            WHERE session_id = @source
            ORDER BY window_id
            """;
        command.Parameters.AddWithValue("@source", sourceSessionId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var ids = new List<string>();
        while (await reader.ReadAsync(ct))
            ids.Add(reader.GetString(0));
        return ids;
    }

    private static async ValueTask ReplaceWindowTabsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SessionMigration migration,
        CancellationToken ct)
    {
        await using (var removeDuplicates = connection.CreateCommand())
        {
            removeDuplicates.Transaction = transaction;
            removeDuplicates.CommandText =
                """
                DELETE FROM terminal_window_tabs
                WHERE session_id = @source
                  AND window_id IN (
                      SELECT window_id
                      FROM terminal_window_tabs
                      WHERE session_id = @replacement)
                """;
            removeDuplicates.Parameters.AddWithValue("@source", migration.SourceSessionId);
            removeDuplicates.Parameters.AddWithValue(
                "@replacement",
                migration.ReplacementSessionId);
            await removeDuplicates.ExecuteNonQueryAsync(ct);
        }

        await using var replace = connection.CreateCommand();
        replace.Transaction = transaction;
        replace.CommandText =
            """
            UPDATE terminal_window_tabs
            SET session_id = @replacement
            WHERE session_id = @source
            """;
        replace.Parameters.AddWithValue("@source", migration.SourceSessionId);
        replace.Parameters.AddWithValue("@replacement", migration.ReplacementSessionId);
        await replace.ExecuteNonQueryAsync(ct);
    }

    private static async ValueTask RestoreWindowTabsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SessionMigration migration,
        CancellationToken ct)
    {
        await using (var removeDuplicates = connection.CreateCommand())
        {
            removeDuplicates.Transaction = transaction;
            removeDuplicates.CommandText =
                """
                DELETE FROM terminal_window_tabs
                WHERE session_id = @replacement
                  AND window_id IN (
                      SELECT window_id
                      FROM terminal_window_tabs
                      WHERE session_id = @source)
                """;
            removeDuplicates.Parameters.AddWithValue("@source", migration.SourceSessionId);
            removeDuplicates.Parameters.AddWithValue(
                "@replacement",
                migration.ReplacementSessionId);
            await removeDuplicates.ExecuteNonQueryAsync(ct);
        }

        await using var restore = connection.CreateCommand();
        restore.Transaction = transaction;
        restore.CommandText =
            """
            UPDATE terminal_window_tabs
            SET session_id = @source
            WHERE session_id = @replacement
            """;
        restore.Parameters.AddWithValue("@source", migration.SourceSessionId);
        restore.Parameters.AddWithValue("@replacement", migration.ReplacementSessionId);
        await restore.ExecuteNonQueryAsync(ct);
    }

    private static async ValueTask DeleteReplacementOverrideAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string replacementSessionId,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "DELETE FROM session_overrides WHERE session_id = @replacement";
        command.Parameters.AddWithValue("@replacement", replacementSessionId);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async ValueTask RefreshCompositionKeysAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<string> windowIds,
        CancellationToken ct)
    {
        foreach (var windowId in windowIds)
        {
            var sessionIds = new List<string>();
            await using (var load = connection.CreateCommand())
            {
                load.Transaction = transaction;
                load.CommandText =
                    """
                    SELECT session_id
                    FROM terminal_window_tabs
                    WHERE window_id = @windowId
                    ORDER BY tab_order
                    """;
                load.Parameters.AddWithValue("@windowId", windowId);
                await using var reader = await load.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                    sessionIds.Add(reader.GetString(0));
            }

            await using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText =
                "UPDATE terminal_windows SET composition_key = @key WHERE id = @windowId";
            update.Parameters.AddWithValue("@key", TerminalWindowComposition.Key(sessionIds));
            update.Parameters.AddWithValue("@windowId", windowId);
            await update.ExecuteNonQueryAsync(ct);
        }
    }

    private static void AddMigrationParameters(
        SqliteCommand command,
        SessionMigration migration)
    {
        command.Parameters.AddWithValue("@id", migration.Id);
        command.Parameters.AddWithValue("@source", migration.SourceSessionId);
        command.Parameters.AddWithValue("@replacement", migration.ReplacementSessionId);
        command.Parameters.AddWithValue("@status", ToStorage(migration.Status));
        command.Parameters.AddWithValue("@path", migration.RecoveryPacketPath);
        command.Parameters.AddWithValue("@bytes", migration.RecoveryPacketBytes);
        command.Parameters.AddWithValue(
            "@truncated",
            migration.RecoveryPacketTruncated ? 1 : 0);
        command.Parameters.AddWithValue(
            "@archivedEventsPath",
            (object?)migration.ArchivedEventsPath ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "@archivedEventsSha256",
            (object?)migration.ArchivedEventsSha256 ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "@baselineTurnCount",
            migration.BaselineTurnCount);
        command.Parameters.AddWithValue(
            "@baselineUpdatedAt",
            migration.BaselineUpdatedAt?.ToString("o") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@error", (object?)migration.Error ?? DBNull.Value);
        command.Parameters.AddWithValue("@createdAt", migration.CreatedAt.ToString("o"));
        command.Parameters.AddWithValue("@updatedAt", migration.UpdatedAt.ToString("o"));
        command.Parameters.AddWithValue(
            "@completedAt",
            migration.CompletedAt?.ToString("o") ?? (object)DBNull.Value);
    }

    private static void AddStatusParameters(
        SqliteCommand command,
        string migrationId,
        SessionMigrationStatus status,
        string? error,
        DateTimeOffset updatedAt,
        DateTimeOffset? completedAt)
    {
        command.Parameters.AddWithValue("@id", migrationId);
        command.Parameters.AddWithValue("@status", ToStorage(status));
        command.Parameters.AddWithValue("@error", (object?)error ?? DBNull.Value);
        command.Parameters.AddWithValue("@updatedAt", updatedAt.ToString("o"));
        command.Parameters.AddWithValue(
            "@completedAt",
            completedAt?.ToString("o") ?? (object)DBNull.Value);
    }

    private static SessionMigration ReadMigration(SqliteDataReader reader) =>
        new(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            FromStorage(reader.GetString(3)),
            reader.GetString(4),
            reader.GetInt64(5),
            reader.GetInt64(6) != 0,
            reader.IsDBNull(11) ? null : reader.GetString(11),
            ParseTimestamp(reader.GetString(12)),
            ParseTimestamp(reader.GetString(13)),
            reader.IsDBNull(14) ? null : ParseTimestamp(reader.GetString(14)))
        {
            ArchivedEventsPath = reader.IsDBNull(7) ? null : reader.GetString(7),
            ArchivedEventsSha256 = reader.IsDBNull(8) ? null : reader.GetString(8),
            BaselineTurnCount = reader.GetInt32(9),
            BaselineUpdatedAt = reader.IsDBNull(10)
                ? null
                : ParseTimestamp(reader.GetString(10)),
        };

    private static string ToStorage(SessionMigrationStatus status) =>
        status switch
        {
            SessionMigrationStatus.Preparing => "preparing",
            SessionMigrationStatus.SessionCreated => "session_created",
            SessionMigrationStatus.CleanupRequired => "cleanup_required",
            SessionMigrationStatus.Completed => "completed",
            SessionMigrationStatus.Failed => "failed",
            _ => "failed",
        };

    private static SessionMigrationStatus FromStorage(string status) =>
        status switch
        {
            "preparing" => SessionMigrationStatus.Preparing,
            "session_created" => SessionMigrationStatus.SessionCreated,
            "cleanup_required" => SessionMigrationStatus.CleanupRequired,
            "completed" => SessionMigrationStatus.Completed,
            _ => SessionMigrationStatus.Failed,
        };

    private static DateTimeOffset ParseTimestamp(string value) =>
        DateTimeOffset.TryParse(value, out var timestamp)
            ? timestamp
            : DateTimeOffset.MinValue;
}
