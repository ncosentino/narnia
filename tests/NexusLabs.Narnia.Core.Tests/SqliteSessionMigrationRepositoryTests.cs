using Microsoft.Data.Sqlite;
using NexusLabs.Narnia.Core.Configuration;
using NexusLabs.Narnia.Core.Models;
using NexusLabs.Narnia.Core.Repositories;
using NexusLabs.Narnia.Core.Services;

namespace NexusLabs.Narnia.Core.Tests;

public sealed class SqliteSessionMigrationRepositoryTests : IDisposable
{
    private const string SourceId = "55555555-5555-4555-8555-555555555555";
    private const string ReplacementId = "66666666-6666-4666-8666-666666666666";
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"narnia_migrations_{Guid.NewGuid():N}.db");
    private readonly NarniaOptions _options;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public SqliteSessionMigrationRepositoryTests()
    {
        _options = new NarniaOptions { SettingsDatabasePath = _databasePath };
        new NarniaSettingsDbMigrator(_options).MigrateUp();
    }

    [Fact]
    public async Task CompleteAsync_CarriesReferencesForwardAndPreservesSource()
    {
        var now = new DateTimeOffset(2026, 7, 24, 12, 0, 0, TimeSpan.Zero);
        const string groupId = "group-1";
        const string collectionId = "collection-1";
        const string windowId = "window-1";
        SeedReferences(now, groupId, collectionId, windowId);
        var repository = new SqliteSessionMigrationRepository(_options);
        var migration = new SessionMigration(
            "migration-1",
            SourceId,
            ReplacementId,
            SessionMigrationStatus.SessionCreated,
            @"C:\narnia\recoveries\recovery.md",
            1234,
            false,
            null,
            now,
            now,
            null);
        await repository.AddAsync(migration, Ct);

        var completed = await repository.CompleteAsync(
            migration.Id,
            now.AddMinutes(1),
            Ct);

        Assert.True(completed);
        var saved = await repository.GetByIdAsync(migration.Id, Ct);
        Assert.NotNull(saved);
        Assert.Equal(SessionMigrationStatus.Completed, saved!.Status);
        await using var connection = new SqliteConnection($"Data Source={_databasePath}");
        await connection.OpenAsync(Ct);
        Assert.Equal(1L, await ScalarAsync(
            connection,
            "SELECT COUNT(*) FROM session_overrides WHERE session_id = @id AND is_favorite = 1 AND display_name = 'Pitcrew'",
            ReplacementId));
        Assert.Equal(1L, await ScalarAsync(
            connection,
            "SELECT COUNT(*) FROM session_overrides WHERE session_id = @id AND is_favorite = 1",
            SourceId));
        Assert.Equal(1L, await ScalarAsync(
            connection,
            "SELECT COUNT(*) FROM work_collection_sessions WHERE collection_id = 'collection-1' AND session_id = @id",
            SourceId));
        Assert.Equal(1L, await ScalarAsync(
            connection,
            "SELECT COUNT(*) FROM work_collection_sessions WHERE collection_id = 'collection-1' AND session_id = @id",
            ReplacementId));
        Assert.Equal(0L, await ScalarAsync(
            connection,
            "SELECT COUNT(*) FROM session_group_members WHERE group_id = 'group-1' AND session_id = @id",
            SourceId));
        Assert.Equal(1L, await ScalarAsync(
            connection,
            "SELECT COUNT(*) FROM session_group_members WHERE group_id = 'group-1' AND session_id = @id",
            ReplacementId));
        Assert.Equal(0L, await ScalarAsync(
            connection,
            "SELECT COUNT(*) FROM terminal_window_tabs WHERE window_id = 'window-1' AND session_id = @id",
            SourceId));
        Assert.Equal(1L, await ScalarAsync(
            connection,
            "SELECT COUNT(*) FROM terminal_window_tabs WHERE window_id = 'window-1' AND session_id = @id",
            ReplacementId));
        var compositionKey = await TextAsync(
            connection,
            "SELECT composition_key FROM terminal_windows WHERE id = 'window-1'");
        Assert.Equal(TerminalWindowComposition.Key([ReplacementId]), compositionKey);

        var reset = await repository.ResetAsync(
            migration.Id,
            "retry",
            now.AddMinutes(2),
            Ct);

        Assert.True(reset);
        var resetMigration = await repository.GetByIdAsync(migration.Id, Ct);
        Assert.Equal(SessionMigrationStatus.Failed, resetMigration!.Status);
        Assert.Equal(0L, await ScalarAsync(
            connection,
            "SELECT COUNT(*) FROM session_overrides WHERE session_id = @id",
            ReplacementId));
        Assert.Equal(0L, await ScalarAsync(
            connection,
            "SELECT COUNT(*) FROM work_collection_sessions WHERE collection_id = 'collection-1' AND session_id = @id",
            ReplacementId));
        Assert.Equal(1L, await ScalarAsync(
            connection,
            "SELECT COUNT(*) FROM session_group_members WHERE group_id = 'group-1' AND session_id = @id",
            SourceId));
        Assert.Equal(0L, await ScalarAsync(
            connection,
            "SELECT COUNT(*) FROM session_group_members WHERE group_id = 'group-1' AND session_id = @id",
            ReplacementId));
        Assert.Equal(1L, await ScalarAsync(
            connection,
            "SELECT COUNT(*) FROM terminal_window_tabs WHERE window_id = 'window-1' AND session_id = @id",
            SourceId));
        Assert.Equal(0L, await ScalarAsync(
            connection,
            "SELECT COUNT(*) FROM terminal_window_tabs WHERE window_id = 'window-1' AND session_id = @id",
            ReplacementId));
        compositionKey = await TextAsync(
            connection,
            "SELECT composition_key FROM terminal_windows WHERE id = 'window-1'");
        Assert.Equal(TerminalWindowComposition.Key([SourceId]), compositionKey);
    }

    [Fact]
    public async Task GetReferenceSummaryAsync_ReturnsMigrationImpact()
    {
        var now = DateTimeOffset.UtcNow;
        SeedReferences(now, "group-2", "collection-2", "window-2");
        var repository = new SqliteSessionMigrationRepository(_options);

        var summary = await repository.GetReferenceSummaryAsync(SourceId, Ct);

        Assert.True(summary.IsFavorite);
        Assert.True(summary.HasAlias);
        Assert.True(summary.HasNotes);
        Assert.Equal(1, summary.CollectionCount);
        Assert.Equal(1, summary.SessionGroupCount);
        Assert.Equal(1, summary.SavedWindowCount);
    }

    [Fact]
    public async Task GetRecoveryProtectedSessionIdsAsync_IncludesFailedAttempts()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new SqliteSessionMigrationRepository(_options);
        await repository.AddAsync(
            new SessionMigration(
                "failed-migration",
                SourceId,
                ReplacementId,
                SessionMigrationStatus.Failed,
                @"C:\narnia\recovery.md",
                100,
                false,
                "failed",
                now,
                now,
                null),
            Ct);

        var protectedSources = await repository.GetRecoveryProtectedSessionIdsAsync(Ct);

        Assert.Contains(SourceId, protectedSources);
        Assert.Contains(ReplacementId, protectedSources);
    }

    [Fact]
    public async Task CompleteAndResetAsync_InPlaceMigration_PreservesExistingReferences()
    {
        var now = DateTimeOffset.UtcNow;
        SeedReferences(now, "group-in-place", "collection-in-place", "window-in-place");
        var repository = new SqliteSessionMigrationRepository(_options);
        var migration = new SessionMigration(
            "in-place-migration",
            SourceId,
            SourceId,
            SessionMigrationStatus.Preparing,
            @"C:\narnia\recovery.md",
            100,
            false,
            null,
            now,
            now,
            null)
        {
            ArchivedEventsPath =
                $@"C:\copilot\session-state\{SourceId}\events.pre-recovery.jsonl",
            ArchivedEventsSha256 = "ABC123",
            BaselineTurnCount = 101,
            BaselineUpdatedAt = now.AddMinutes(-1),
        };
        await repository.AddAsync(migration, Ct);

        Assert.True(await repository.CompleteAsync(migration.Id, now.AddMinutes(1), Ct));
        var completed = await repository.GetByIdAsync(migration.Id, Ct);

        Assert.NotNull(completed);
        Assert.True(completed!.IsInPlace);
        Assert.Equal(SessionMigrationStatus.Completed, completed.Status);
        Assert.Equal("ABC123", completed.ArchivedEventsSha256);
        Assert.Equal(101, completed.BaselineTurnCount);
        Assert.Equal(now.AddMinutes(-1), completed.BaselineUpdatedAt);
        Assert.True(await repository.ResetAsync(
            migration.Id,
            "reset",
            now.AddMinutes(2),
            Ct));

        await using var connection = new SqliteConnection($"Data Source={_databasePath}");
        await connection.OpenAsync(Ct);
        Assert.Equal(1L, await ScalarAsync(
            connection,
            "SELECT COUNT(*) FROM session_overrides WHERE session_id = @id",
            SourceId));
        Assert.Equal(1L, await ScalarAsync(
            connection,
            "SELECT COUNT(*) FROM work_collection_sessions WHERE collection_id = 'collection-in-place' AND session_id = @id",
            SourceId));
        Assert.Equal(1L, await ScalarAsync(
            connection,
            "SELECT COUNT(*) FROM session_group_members WHERE group_id = 'group-in-place' AND session_id = @id",
            SourceId));
        Assert.Equal(1L, await ScalarAsync(
            connection,
            "SELECT COUNT(*) FROM terminal_window_tabs WHERE window_id = 'window-in-place' AND session_id = @id",
            SourceId));

        var retry = migration with
        {
            Status = SessionMigrationStatus.Preparing,
            RecoveryPacketPath = @"C:\narnia\retry.md",
            RecoveryPacketBytes = 200,
            CreatedAt = now.AddMinutes(3),
            UpdatedAt = now.AddMinutes(3),
            Error = null,
            CompletedAt = null,
            BaselineTurnCount = 102,
            BaselineUpdatedAt = now.AddMinutes(2),
        };
        Assert.True(await repository.RestartAsync(retry, Ct));
        var restarted = await repository.GetByIdAsync(migration.Id, Ct);
        Assert.Equal(SessionMigrationStatus.Preparing, restarted!.Status);
        Assert.Equal(@"C:\narnia\retry.md", restarted.RecoveryPacketPath);
        Assert.Equal(102, restarted.BaselineTurnCount);
    }

    private void SeedReferences(
        DateTimeOffset now,
        string groupId,
        string collectionId,
        string windowId)
    {
        using var connection = new SqliteConnection($"Data Source={_databasePath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT OR REPLACE INTO session_overrides (
                session_id, display_name, repository, branch, notes, created_at, updated_at,
                is_archived, local_path, terminal_title, is_favorite)
            VALUES (
                @source, 'Pitcrew', 'ncosentino/pitcrew', 'main', 'Recovery notes', @now, @now,
                0, 'C:\repo', 'Pitcrew', 1);
            INSERT INTO work_collections (id, name, name_key, created_at, updated_at)
            VALUES (@collection, 'Work', 'work', @now, @now);
            INSERT INTO work_collection_sessions (collection_id, session_id, added_at)
            VALUES (@collection, @source, @now);
            INSERT INTO session_groups (id, name, created_at, updated_at)
            VALUES (@group, 'Window', @now, @now);
            INSERT INTO session_group_members (group_id, session_id, member_order)
            VALUES (@group, @source, 0);
            INSERT INTO terminal_windows (
                id, name, pinned, source, status, terminal_pid, composition_key,
                occurrence_count, first_seen_at, last_seen_at, closed_at)
            VALUES (
                @window, 'Saved', 1, 'snapshot', 'closed', NULL, 'old-key',
                1, @now, @now, @now);
            INSERT INTO terminal_window_tabs (window_id, session_id, tab_order, directory)
            VALUES (@window, @source, 0, 'C:\repo');
            """;
        command.Parameters.AddWithValue("@source", SourceId);
        command.Parameters.AddWithValue("@collection", collectionId);
        command.Parameters.AddWithValue("@group", groupId);
        command.Parameters.AddWithValue("@window", windowId);
        command.Parameters.AddWithValue("@now", now.ToString("o"));
        command.ExecuteNonQuery();
    }

    private static async Task<long> ScalarAsync(
        SqliteConnection connection,
        string sql,
        string id)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@id", id);
        return (long)(await command.ExecuteScalarAsync(Ct) ?? 0L);
    }

    private static async Task<string?> TextAsync(
        SqliteConnection connection,
        string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync(Ct) as string;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_databasePath))
            File.Delete(_databasePath);
    }
}
