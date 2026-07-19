using Microsoft.Data.Sqlite;
using NexusLabs.Narnia.Core.Configuration;
using NexusLabs.Narnia.Core.Models;
using NexusLabs.Narnia.Core.Repositories;

namespace NexusLabs.Narnia.Core.Tests;

public sealed class SqliteSessionStorageRepositoryTests : IDisposable
{
    private readonly SqliteConnection _keepAlive;
    private readonly SqliteSessionStorageRepository _repository;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public SqliteSessionStorageRepositoryTests()
    {
        var databaseName = $"narnia_storage_test_{Guid.NewGuid():N}";
        var connectionString = $"Data Source={databaseName};Mode=Memory;Cache=Shared";
        _keepAlive = new SqliteConnection(connectionString);
        _keepAlive.Open();
        ApplyMigration("0011_add_session_storage.sql");
        _repository = new SqliteSessionStorageRepository(new NarniaOptions
        {
            SettingsConnectionString = connectionString,
        });
    }

    public void Dispose() => _keepAlive.Dispose();

    [Fact]
    public async Task SaveScanAsync_StoresCurrentDailyAndScanMetadata()
    {
        var started = new DateTimeOffset(2026, 7, 19, 12, 0, 0, TimeSpan.Zero);
        var completed = started.AddMinutes(2);

        await _repository.SaveScanAsync(
            [Record("session-1", started, 100), Record("session-2", started, 250)],
            started,
            completed,
            Ct);

        var current = await _repository.GetCurrentAsync(Ct);
        var daily = await _repository.GetDailyAsync(30, Ct);
        var scan = await _repository.GetLastScanAsync(Ct);

        Assert.Equal(2, current.Count);
        Assert.Equal(350, current.Sum(record => record.TotalBytes));
        var snapshot = Assert.Single(daily);
        Assert.Equal(DateOnly.FromDateTime(completed.UtcDateTime), snapshot.SnapshotDate);
        Assert.Equal(350, snapshot.Categories.TotalBytes);
        Assert.NotNull(scan);
        Assert.Equal("completed", scan!.Status);
        Assert.Equal(2, scan.CompleteCount);
    }

    [Fact]
    public async Task SaveScanAsync_TracksPreviousSizeAndRemovesMissingSessions()
    {
        var first = new DateTimeOffset(2026, 7, 19, 12, 0, 0, TimeSpan.Zero);
        await _repository.SaveScanAsync(
            [Record("session-1", first, 100), Record("session-2", first, 200)],
            first,
            first.AddMinutes(1),
            Ct);
        var second = first.AddDays(1);

        await _repository.SaveScanAsync(
            [Record("session-1", second, 160)],
            second,
            second.AddMinutes(1),
            Ct);

        var record = Assert.Single(await _repository.GetCurrentAsync(Ct));
        Assert.Equal("session-1", record.SessionId);
        Assert.Equal(100, record.PreviousTotalBytes);
        Assert.Equal(60, record.GrowthBytes);
        Assert.Equal(2, (await _repository.GetDailyAsync(30, Ct)).Count);
    }

    [Fact]
    public async Task RecordScanFailureAsync_PreservesCurrentMeasurements()
    {
        var started = new DateTimeOffset(2026, 7, 19, 12, 0, 0, TimeSpan.Zero);
        await _repository.SaveScanAsync(
            [Record("session-1", started, 100)],
            started,
            started.AddMinutes(1),
            Ct);

        await _repository.RecordScanFailureAsync(
            started.AddHours(1),
            started.AddHours(1).AddMinutes(1),
            "scan failed",
            Ct);

        Assert.Single(await _repository.GetCurrentAsync(Ct));
        var scan = await _repository.GetLastScanAsync(Ct);
        Assert.Equal("failed", scan!.Status);
        Assert.Equal("scan failed", scan.Error);
    }

    [Fact]
    public async Task CleanupPersistence_RemovesCurrentAndRecordsAudit()
    {
        var now = new DateTimeOffset(2026, 7, 19, 12, 0, 0, TimeSpan.Zero);
        await _repository.SaveScanAsync(
            [Record("session-1", now, 100)],
            now,
            now.AddMinutes(1),
            Ct);

        await _repository.RecordCleanupAsync(
            [new SessionCleanupAuditEntry(
                "audit-1",
                "session-1",
                now,
                now.AddMinutes(2),
                100,
                "deleted",
                null)],
            Ct);
        await _repository.RemoveCurrentAsync(["session-1"], Ct);

        Assert.Null(await _repository.GetBySessionIdAsync("session-1", Ct));
        var audit = Assert.Single(await _repository.GetRecentCleanupAsync(10, Ct));
        Assert.Equal("deleted", audit.Result);
        await using var command = _keepAlive.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM session_cleanup_audit";
        Assert.Equal(1L, await command.ExecuteScalarAsync(Ct));
    }

    private static SessionStorageRecord Record(
        string sessionId,
        DateTimeOffset scannedAt,
        long totalBytes) =>
        new()
        {
            SessionId = sessionId,
            ScannedAt = scannedAt,
            TotalBytes = totalBytes,
            FileCount = 1,
            LastWriteAt = scannedAt,
            EventsBytes = totalBytes,
            SessionDatabaseBytes = 0,
            CheckpointsBytes = 0,
            RewindBytes = 0,
            ArtifactsBytes = 0,
            OtherBytes = 0,
            LargestFileBytes = totalBytes,
            LargestFilePath = "events.jsonl",
            IsComplete = true,
            IsUserNamed = false,
            ContainsGitRepository = false,
            ContainsLinkedWorktree = false,
            ContainsReparsePoint = false,
        };

    private void ApplyMigration(string fileName)
    {
        var assembly = typeof(SqliteSessionStorageRepository).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith(fileName, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        using var command = _keepAlive.CreateCommand();
        command.CommandText = reader.ReadToEnd();
        command.ExecuteNonQuery();
    }
}
