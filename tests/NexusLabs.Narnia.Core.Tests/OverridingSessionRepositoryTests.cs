using Microsoft.Data.Sqlite;
using Moq;
using NexusLabs.Narnia.Core.Configuration;
using NexusLabs.Narnia.Core.Models;
using NexusLabs.Narnia.Core.Repositories;

namespace NexusLabs.Narnia.Core.Tests;

public sealed class OverridingSessionRepositoryTests : IDisposable
{
    private readonly SqliteConnection _keepAlive;
    private readonly Mock<ISessionOverridesRepository> _overrides;
    private readonly OverridingSessionRepository _repository;

    public OverridingSessionRepositoryTests()
    {
        var dbName = $"narnia_test_{Guid.NewGuid():N}";
        var connectionString = $"Data Source={dbName};Mode=Memory;Cache=Shared";

        // Keep one connection open so the in-memory DB survives for the test lifetime
        _keepAlive = new SqliteConnection(connectionString);
        _keepAlive.Open();
        CreateSchema();
        SeedData();

        var inner = new SqliteSessionRepository(new NarniaOptions { ConnectionString = connectionString });

        _overrides = new Mock<ISessionOverridesRepository>();
        _overrides
            .Setup(o => o.GetOverrideAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SessionOverride?)null);

        _repository = new OverridingSessionRepository(inner, _overrides.Object);
    }

    public void Dispose() => _keepAlive.Dispose();

    private void CreateSchema()
    {
        using var cmd = _keepAlive.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE sessions (
                id TEXT PRIMARY KEY,
                cwd TEXT,
                repository TEXT,
                branch TEXT,
                summary TEXT,
                created_at TEXT,
                updated_at TEXT,
                host_type TEXT
            );
            CREATE TABLE turns (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                session_id TEXT NOT NULL,
                turn_index INTEGER NOT NULL,
                user_message TEXT,
                assistant_response TEXT,
                timestamp TEXT
            );
            CREATE TABLE checkpoints (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                session_id TEXT NOT NULL,
                checkpoint_number INTEGER NOT NULL,
                title TEXT,
                overview TEXT,
                history TEXT,
                work_done TEXT,
                technical_details TEXT,
                important_files TEXT,
                next_steps TEXT,
                created_at TEXT
            );
            CREATE TABLE session_refs (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                session_id TEXT NOT NULL,
                ref_type TEXT,
                ref_value TEXT,
                turn_index INTEGER,
                created_at TEXT
            );
            CREATE VIRTUAL TABLE search_index USING fts5(
                content, session_id UNINDEXED, source_type UNINDEXED, source_id UNINDEXED
            );
            """;
        cmd.ExecuteNonQuery();
    }

    private void SeedData()
    {
        using var cmd = _keepAlive.CreateCommand();
        cmd.CommandText = """
            INSERT INTO sessions (id, cwd, repository, branch, summary, created_at, updated_at) VALUES
                ('sess-1', 'C:\dev\proj-a', 'owner/repo-a', 'main', 'Raw summary', '2025-01-01T10:00:00Z', '2025-01-02T12:00:00Z'),
                ('sess-2', 'C:\dev\proj-b', 'owner/repo-b', 'feature/x', 'Fix the tests', '2025-01-03T09:00:00Z', '2025-01-03T11:00:00Z');

            INSERT INTO session_refs (session_id, ref_type, ref_value, turn_index, created_at) VALUES
                ('sess-1', 'commit', 'abc123', 1, '2025-01-01T10:06:00Z');

            INSERT INTO search_index (content, session_id, source_type, source_id) VALUES
                ('Committed deadc0de1234567890 work done', 'sess-2', 'checkpoint_work_done', '1');
            """;
        cmd.ExecuteNonQuery();
    }

    [Fact]
    public async Task GetSessionsByRefAsync_NoOverride_ReturnsRawSessionData()
    {
        var results = await _repository.GetSessionsByRefAsync("abc123", TestContext.Current.CancellationToken);

        Assert.Single(results);
        Assert.Equal("Raw summary", results[0].Session.Summary);
        Assert.Equal("owner/repo-a", results[0].Session.Repository);
        Assert.Equal("main", results[0].Session.Branch);
    }

    [Fact]
    public async Task GetSessionsByRefAsync_WithOverride_MergesDisplayNameRepositoryAndBranch()
    {
        var ov = new SessionOverride(
            "sess-1", "Custom Name", "custom/repo", "custom-branch", null,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        _overrides
            .Setup(o => o.GetOverrideAsync("sess-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ov);

        var results = await _repository.GetSessionsByRefAsync("abc123", TestContext.Current.CancellationToken);

        Assert.Single(results);
        Assert.Equal("Custom Name", results[0].Session.Summary);
        Assert.Equal("custom/repo", results[0].Session.Repository);
        Assert.Equal("custom-branch", results[0].Session.Branch);
    }

    [Fact]
    public async Task GetSessionsByRefAsync_WithOverride_PreservesMatchConfidence()
    {
        // Merging the override must not disturb the Confirmed/Mentioned classification.
        var ov = new SessionOverride(
            "sess-1", "Custom Name", null, null, null,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        _overrides
            .Setup(o => o.GetOverrideAsync("sess-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ov);

        var results = await _repository.GetSessionsByRefAsync("abc123", TestContext.Current.CancellationToken);

        Assert.Single(results);
        Assert.Equal(CommitMatchConfidence.Confirmed, results[0].Confidence);
    }

    [Fact]
    public async Task GetSessionsByRefAsync_TextOnlyMatch_OverrideStillApplied()
    {
        // "deadc0de..." only matches sess-2 via the FTS fallback (Mentioned), not session_refs.
        var ov = new SessionOverride(
            "sess-2", null, "overridden/repo", null, null,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        _overrides
            .Setup(o => o.GetOverrideAsync("sess-2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ov);

        var results = await _repository.GetSessionsByRefAsync("deadc0de", TestContext.Current.CancellationToken);

        Assert.Single(results);
        Assert.Equal("overridden/repo", results[0].Session.Repository);
        Assert.Equal(CommitMatchConfidence.Mentioned, results[0].Confidence);
    }
}
