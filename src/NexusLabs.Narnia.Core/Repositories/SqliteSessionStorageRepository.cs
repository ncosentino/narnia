using Microsoft.Data.Sqlite;
using NexusLabs.Narnia.Core.Configuration;
using NexusLabs.Narnia.Core.Models;

namespace NexusLabs.Narnia.Core.Repositories;

/// <summary>SQLite persistence for Narnia-owned session-storage measurements.</summary>
public sealed class SqliteSessionStorageRepository(NarniaOptions options) : ISessionStorageRepository
{
    private readonly string _connectionString = options.SettingsConnectionString
        ?? $"Data Source={options.SettingsDatabasePath}";

    /// <inheritdoc />
    public async ValueTask SaveScanAsync(
        IReadOnlyList<SessionStorageRecord> records,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt,
        CancellationToken ct)
    {
        var scanId = Guid.NewGuid().ToString();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var transaction =
            (SqliteTransaction)await connection.BeginTransactionAsync(ct);

        await using (var upsert = connection.CreateCommand())
        {
            upsert.Transaction = transaction;
            upsert.CommandText =
                """
                INSERT INTO session_storage_current (
                    session_id, scan_id, scanned_at, previous_scanned_at,
                    total_bytes, previous_total_bytes, file_count, last_write_at,
                    events_bytes, session_database_bytes, checkpoints_bytes,
                    rewind_bytes, artifacts_bytes, other_bytes,
                    largest_file_bytes, largest_file_path, is_complete, error,
                    is_user_named, contains_git_repository,
                    contains_linked_worktree, contains_reparse_point)
                VALUES (
                    @sessionId, @scanId, @scannedAt, NULL,
                    @totalBytes, NULL, @fileCount, @lastWriteAt,
                    @eventsBytes, @sessionDatabaseBytes, @checkpointsBytes,
                    @rewindBytes, @artifactsBytes, @otherBytes,
                    @largestFileBytes, @largestFilePath, @isComplete, @error,
                    @isUserNamed, @containsGitRepository,
                    @containsLinkedWorktree, @containsReparsePoint)
                ON CONFLICT(session_id) DO UPDATE SET
                    scan_id = excluded.scan_id,
                    previous_scanned_at = session_storage_current.scanned_at,
                    previous_total_bytes = session_storage_current.total_bytes,
                    scanned_at = excluded.scanned_at,
                    total_bytes = excluded.total_bytes,
                    file_count = excluded.file_count,
                    last_write_at = excluded.last_write_at,
                    events_bytes = excluded.events_bytes,
                    session_database_bytes = excluded.session_database_bytes,
                    checkpoints_bytes = excluded.checkpoints_bytes,
                    rewind_bytes = excluded.rewind_bytes,
                    artifacts_bytes = excluded.artifacts_bytes,
                    other_bytes = excluded.other_bytes,
                    largest_file_bytes = excluded.largest_file_bytes,
                    largest_file_path = excluded.largest_file_path,
                    is_complete = excluded.is_complete,
                    error = excluded.error,
                    is_user_named = excluded.is_user_named,
                    contains_git_repository = excluded.contains_git_repository,
                    contains_linked_worktree = excluded.contains_linked_worktree,
                    contains_reparse_point = excluded.contains_reparse_point
                """;
            AddScanParameters(upsert);

            foreach (var record in records)
            {
                SetScanParameters(upsert, scanId, record);
                await upsert.ExecuteNonQueryAsync(ct);
            }
        }

        await using (var deleteStale = connection.CreateCommand())
        {
            deleteStale.Transaction = transaction;
            deleteStale.CommandText =
                "DELETE FROM session_storage_current WHERE scan_id <> @scanId";
            deleteStale.Parameters.AddWithValue("@scanId", scanId);
            await deleteStale.ExecuteNonQueryAsync(ct);
        }

        var categories = SumCategories(records);
        await using (var daily = connection.CreateCommand())
        {
            daily.Transaction = transaction;
            daily.CommandText =
                """
                INSERT INTO session_storage_daily (
                    snapshot_date, scanned_at, session_count, total_bytes,
                    events_bytes, session_database_bytes, checkpoints_bytes,
                    rewind_bytes, artifacts_bytes, other_bytes)
                VALUES (
                    @date, @scannedAt, @sessionCount, @totalBytes,
                    @eventsBytes, @sessionDatabaseBytes, @checkpointsBytes,
                    @rewindBytes, @artifactsBytes, @otherBytes)
                ON CONFLICT(snapshot_date) DO UPDATE SET
                    scanned_at = excluded.scanned_at,
                    session_count = excluded.session_count,
                    total_bytes = excluded.total_bytes,
                    events_bytes = excluded.events_bytes,
                    session_database_bytes = excluded.session_database_bytes,
                    checkpoints_bytes = excluded.checkpoints_bytes,
                    rewind_bytes = excluded.rewind_bytes,
                    artifacts_bytes = excluded.artifacts_bytes,
                    other_bytes = excluded.other_bytes
                """;
            daily.Parameters.AddWithValue("@date", DateOnly.FromDateTime(completedAt.UtcDateTime).ToString("O"));
            daily.Parameters.AddWithValue("@scannedAt", completedAt.ToString("O"));
            daily.Parameters.AddWithValue("@sessionCount", records.Count);
            daily.Parameters.AddWithValue("@totalBytes", categories.TotalBytes);
            daily.Parameters.AddWithValue("@eventsBytes", categories.EventsBytes);
            daily.Parameters.AddWithValue("@sessionDatabaseBytes", categories.SessionDatabaseBytes);
            daily.Parameters.AddWithValue("@checkpointsBytes", categories.CheckpointsBytes);
            daily.Parameters.AddWithValue("@rewindBytes", categories.RewindBytes);
            daily.Parameters.AddWithValue("@artifactsBytes", categories.ArtifactsBytes);
            daily.Parameters.AddWithValue("@otherBytes", categories.OtherBytes);
            await daily.ExecuteNonQueryAsync(ct);
        }

        await UpsertScanInfoAsync(
            connection,
            transaction,
            new SessionStorageScanInfo(
                "completed",
                startedAt,
                completedAt,
                records.Count,
                records.Count(record => record.IsComplete),
                null),
            ct);

        await transaction.CommitAsync(ct);
    }

    /// <inheritdoc />
    public async ValueTask RecordScanFailureAsync(
        DateTimeOffset startedAt,
        DateTimeOffset completedAt,
        string error,
        CancellationToken ct)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var transaction =
            (SqliteTransaction)await connection.BeginTransactionAsync(ct);
        await UpsertScanInfoAsync(
            connection,
            transaction,
            new SessionStorageScanInfo(
                "failed",
                startedAt,
                completedAt,
                0,
                0,
                error),
            ct);
        await transaction.CommitAsync(ct);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<SessionStorageRecord>> GetCurrentAsync(CancellationToken ct)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                session_id, scanned_at, previous_scanned_at,
                total_bytes, previous_total_bytes, file_count, last_write_at,
                events_bytes, session_database_bytes, checkpoints_bytes,
                rewind_bytes, artifacts_bytes, other_bytes,
                largest_file_bytes, largest_file_path, is_complete, error,
                is_user_named, contains_git_repository,
                contains_linked_worktree, contains_reparse_point
            FROM session_storage_current
            ORDER BY total_bytes DESC, session_id
            """;

        await using var reader = await command.ExecuteReaderAsync(ct);
        var records = new List<SessionStorageRecord>();
        while (await reader.ReadAsync(ct))
            records.Add(ReadRecord(reader));
        return records;
    }

    /// <inheritdoc />
    public async ValueTask<SessionStorageRecord?> GetBySessionIdAsync(
        string sessionId,
        CancellationToken ct)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                session_id, scanned_at, previous_scanned_at,
                total_bytes, previous_total_bytes, file_count, last_write_at,
                events_bytes, session_database_bytes, checkpoints_bytes,
                rewind_bytes, artifacts_bytes, other_bytes,
                largest_file_bytes, largest_file_path, is_complete, error,
                is_user_named, contains_git_repository,
                contains_linked_worktree, contains_reparse_point
            FROM session_storage_current
            WHERE session_id = @sessionId
            """;
        command.Parameters.AddWithValue("@sessionId", sessionId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadRecord(reader) : null;
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<SessionStorageDailySnapshot>> GetDailyAsync(
        int days,
        CancellationToken ct)
    {
        if (days <= 0)
            return [];

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                snapshot_date, scanned_at, session_count,
                events_bytes, session_database_bytes, checkpoints_bytes,
                rewind_bytes, artifacts_bytes, other_bytes
            FROM session_storage_daily
            ORDER BY snapshot_date DESC
            LIMIT @days
            """;
        command.Parameters.AddWithValue("@days", days);

        await using var reader = await command.ExecuteReaderAsync(ct);
        var snapshots = new List<SessionStorageDailySnapshot>();
        while (await reader.ReadAsync(ct))
        {
            snapshots.Add(new SessionStorageDailySnapshot(
                DateOnly.Parse(reader.GetString(0)),
                DateTimeOffset.Parse(reader.GetString(1)),
                reader.GetInt32(2),
                new SessionStorageCategoryTotals(
                    reader.GetInt64(3),
                    reader.GetInt64(4),
                    reader.GetInt64(5),
                    reader.GetInt64(6),
                    reader.GetInt64(7),
                    reader.GetInt64(8))));
        }

        snapshots.Reverse();
        return snapshots;
    }

    /// <inheritdoc />
    public async ValueTask<SessionStorageScanInfo?> GetLastScanAsync(CancellationToken ct)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT status, started_at, completed_at, session_count, complete_count, error
            FROM session_storage_scan
            WHERE id = 1
            """;
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;

        return new SessionStorageScanInfo(
            reader.GetString(0),
            DateTimeOffset.Parse(reader.GetString(1)),
            DateTimeOffset.Parse(reader.GetString(2)),
            reader.GetInt32(3),
            reader.GetInt32(4),
            reader.IsDBNull(5) ? null : reader.GetString(5));
    }

    /// <inheritdoc />
    public async ValueTask RemoveCurrentAsync(
        IReadOnlyCollection<string> sessionIds,
        CancellationToken ct)
    {
        if (sessionIds.Count == 0)
            return;

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var transaction =
            (SqliteTransaction)await connection.BeginTransactionAsync(ct);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "DELETE FROM session_storage_current WHERE session_id = @sessionId";
        var parameter = command.Parameters.Add("@sessionId", SqliteType.Text);
        foreach (var sessionId in sessionIds)
        {
            parameter.Value = sessionId;
            await command.ExecuteNonQueryAsync(ct);
        }
        await transaction.CommitAsync(ct);
    }

    /// <inheritdoc />
    public async ValueTask RecordCleanupAsync(
        IReadOnlyCollection<SessionCleanupAuditEntry> entries,
        CancellationToken ct)
    {
        if (entries.Count == 0)
            return;

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var transaction =
            (SqliteTransaction)await connection.BeginTransactionAsync(ct);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO session_cleanup_audit (
                id, session_id, requested_at, completed_at,
                estimated_bytes, result, error)
            VALUES (
                @id, @sessionId, @requestedAt, @completedAt,
                @estimatedBytes, @result, @error)
            """;
        command.Parameters.Add("@id", SqliteType.Text);
        command.Parameters.Add("@sessionId", SqliteType.Text);
        command.Parameters.Add("@requestedAt", SqliteType.Text);
        command.Parameters.Add("@completedAt", SqliteType.Text);
        command.Parameters.Add("@estimatedBytes", SqliteType.Integer);
        command.Parameters.Add("@result", SqliteType.Text);
        command.Parameters.Add("@error", SqliteType.Text);

        foreach (var entry in entries)
        {
            command.Parameters["@id"].Value = entry.Id;
            command.Parameters["@sessionId"].Value = entry.SessionId;
            command.Parameters["@requestedAt"].Value = entry.RequestedAt.ToString("O");
            command.Parameters["@completedAt"].Value = entry.CompletedAt.ToString("O");
            command.Parameters["@estimatedBytes"].Value = entry.EstimatedBytes;
            command.Parameters["@result"].Value = entry.Result;
            command.Parameters["@error"].Value = (object?)entry.Error ?? DBNull.Value;
            await command.ExecuteNonQueryAsync(ct);
        }

        await transaction.CommitAsync(ct);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<SessionCleanupAuditEntry>> GetRecentCleanupAsync(
        int limit,
        CancellationToken ct)
    {
        if (limit <= 0)
            return [];

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                id, session_id, requested_at, completed_at,
                estimated_bytes, result, error
            FROM session_cleanup_audit
            ORDER BY completed_at DESC, id
            LIMIT @limit
            """;
        command.Parameters.AddWithValue("@limit", limit);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var entries = new List<SessionCleanupAuditEntry>();
        while (await reader.ReadAsync(ct))
        {
            entries.Add(new SessionCleanupAuditEntry(
                reader.GetString(0),
                reader.GetString(1),
                DateTimeOffset.Parse(reader.GetString(2)),
                DateTimeOffset.Parse(reader.GetString(3)),
                reader.GetInt64(4),
                reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6)));
        }
        return entries;
    }

    private static void AddScanParameters(SqliteCommand command)
    {
        command.Parameters.Add("@sessionId", SqliteType.Text);
        command.Parameters.Add("@scanId", SqliteType.Text);
        command.Parameters.Add("@scannedAt", SqliteType.Text);
        command.Parameters.Add("@totalBytes", SqliteType.Integer);
        command.Parameters.Add("@fileCount", SqliteType.Integer);
        command.Parameters.Add("@lastWriteAt", SqliteType.Text);
        command.Parameters.Add("@eventsBytes", SqliteType.Integer);
        command.Parameters.Add("@sessionDatabaseBytes", SqliteType.Integer);
        command.Parameters.Add("@checkpointsBytes", SqliteType.Integer);
        command.Parameters.Add("@rewindBytes", SqliteType.Integer);
        command.Parameters.Add("@artifactsBytes", SqliteType.Integer);
        command.Parameters.Add("@otherBytes", SqliteType.Integer);
        command.Parameters.Add("@largestFileBytes", SqliteType.Integer);
        command.Parameters.Add("@largestFilePath", SqliteType.Text);
        command.Parameters.Add("@isComplete", SqliteType.Integer);
        command.Parameters.Add("@error", SqliteType.Text);
        command.Parameters.Add("@isUserNamed", SqliteType.Integer);
        command.Parameters.Add("@containsGitRepository", SqliteType.Integer);
        command.Parameters.Add("@containsLinkedWorktree", SqliteType.Integer);
        command.Parameters.Add("@containsReparsePoint", SqliteType.Integer);
    }

    private static void SetScanParameters(
        SqliteCommand command,
        string scanId,
        SessionStorageRecord record)
    {
        command.Parameters["@sessionId"].Value = record.SessionId;
        command.Parameters["@scanId"].Value = scanId;
        command.Parameters["@scannedAt"].Value = record.ScannedAt.ToString("O");
        command.Parameters["@totalBytes"].Value = record.TotalBytes;
        command.Parameters["@fileCount"].Value = record.FileCount;
        command.Parameters["@lastWriteAt"].Value =
            record.LastWriteAt is null ? DBNull.Value : record.LastWriteAt.Value.ToString("O");
        command.Parameters["@eventsBytes"].Value = record.EventsBytes;
        command.Parameters["@sessionDatabaseBytes"].Value = record.SessionDatabaseBytes;
        command.Parameters["@checkpointsBytes"].Value = record.CheckpointsBytes;
        command.Parameters["@rewindBytes"].Value = record.RewindBytes;
        command.Parameters["@artifactsBytes"].Value = record.ArtifactsBytes;
        command.Parameters["@otherBytes"].Value = record.OtherBytes;
        command.Parameters["@largestFileBytes"].Value = record.LargestFileBytes;
        command.Parameters["@largestFilePath"].Value =
            (object?)record.LargestFilePath ?? DBNull.Value;
        command.Parameters["@isComplete"].Value = record.IsComplete ? 1 : 0;
        command.Parameters["@error"].Value = (object?)record.Error ?? DBNull.Value;
        command.Parameters["@isUserNamed"].Value = record.IsUserNamed ? 1 : 0;
        command.Parameters["@containsGitRepository"].Value =
            record.ContainsGitRepository ? 1 : 0;
        command.Parameters["@containsLinkedWorktree"].Value =
            record.ContainsLinkedWorktree ? 1 : 0;
        command.Parameters["@containsReparsePoint"].Value =
            record.ContainsReparsePoint ? 1 : 0;
    }

    private static SessionStorageRecord ReadRecord(SqliteDataReader reader) =>
        new()
        {
            SessionId = reader.GetString(0),
            ScannedAt = DateTimeOffset.Parse(reader.GetString(1)),
            PreviousScannedAt =
                reader.IsDBNull(2) ? null : DateTimeOffset.Parse(reader.GetString(2)),
            TotalBytes = reader.GetInt64(3),
            PreviousTotalBytes = reader.IsDBNull(4) ? null : reader.GetInt64(4),
            FileCount = reader.GetInt64(5),
            LastWriteAt =
                reader.IsDBNull(6) ? null : DateTimeOffset.Parse(reader.GetString(6)),
            EventsBytes = reader.GetInt64(7),
            SessionDatabaseBytes = reader.GetInt64(8),
            CheckpointsBytes = reader.GetInt64(9),
            RewindBytes = reader.GetInt64(10),
            ArtifactsBytes = reader.GetInt64(11),
            OtherBytes = reader.GetInt64(12),
            LargestFileBytes = reader.GetInt64(13),
            LargestFilePath = reader.IsDBNull(14) ? null : reader.GetString(14),
            IsComplete = reader.GetInt64(15) != 0,
            Error = reader.IsDBNull(16) ? null : reader.GetString(16),
            IsUserNamed = reader.GetInt64(17) != 0,
            ContainsGitRepository = reader.GetInt64(18) != 0,
            ContainsLinkedWorktree = reader.GetInt64(19) != 0,
            ContainsReparsePoint = reader.GetInt64(20) != 0,
        };

    private static SessionStorageCategoryTotals SumCategories(
        IEnumerable<SessionStorageRecord> records) =>
        new(
            records.Sum(record => record.EventsBytes),
            records.Sum(record => record.SessionDatabaseBytes),
            records.Sum(record => record.CheckpointsBytes),
            records.Sum(record => record.RewindBytes),
            records.Sum(record => record.ArtifactsBytes),
            records.Sum(record => record.OtherBytes));

    private static async ValueTask UpsertScanInfoAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SessionStorageScanInfo scan,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO session_storage_scan (
                id, status, started_at, completed_at,
                session_count, complete_count, error)
            VALUES (
                1, @status, @startedAt, @completedAt,
                @sessionCount, @completeCount, @error)
            ON CONFLICT(id) DO UPDATE SET
                status = excluded.status,
                started_at = excluded.started_at,
                completed_at = excluded.completed_at,
                session_count = excluded.session_count,
                complete_count = excluded.complete_count,
                error = excluded.error
            """;
        command.Parameters.AddWithValue("@status", scan.Status);
        command.Parameters.AddWithValue("@startedAt", scan.StartedAt.ToString("O"));
        command.Parameters.AddWithValue("@completedAt", scan.CompletedAt.ToString("O"));
        command.Parameters.AddWithValue("@sessionCount", scan.SessionCount);
        command.Parameters.AddWithValue("@completeCount", scan.CompleteCount);
        command.Parameters.AddWithValue("@error", (object?)scan.Error ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(ct);
    }
}
