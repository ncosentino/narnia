using Microsoft.Data.Sqlite;
using Moq;
using NexusLabs.Narnia.Core.Configuration;
using NexusLabs.Narnia.Core.Models;
using NexusLabs.Narnia.Core.Repositories;

namespace NexusLabs.Narnia.Core.Tests;

public sealed class OverridingSessionRepositoryTests : IDisposable
{
    private readonly SqliteConnection _keepAlive;
    private readonly MockRepository _mocks = new(MockBehavior.Strict);
    private readonly Mock<ISessionOverridesRepository> _overrides;
    private readonly SqliteSessionRepository _inner;
    private readonly OverridingSessionRepository _repository;
    private readonly OverridingSessionSearch _search;
    private readonly Dictionary<string, SessionOverride> _savedOverrides = new(StringComparer.Ordinal);

    public OverridingSessionRepositoryTests()
    {
        var dbName = $"narnia_test_{Guid.NewGuid():N}";
        var connectionString = $"Data Source={dbName};Mode=Memory;Cache=Shared";

        // Keep one connection open so the in-memory DB survives for the test lifetime
        _keepAlive = new SqliteConnection(connectionString);
        _keepAlive.Open();
        CreateSchema();
        SeedData();

        _inner = new SqliteSessionRepository(new NarniaOptions { ConnectionString = connectionString });

        _overrides = _mocks.Create<ISessionOverridesRepository>();
        _overrides
            .Setup(o => o.GetAllOverridesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => (IReadOnlyDictionary<string, SessionOverride>)
                new Dictionary<string, SessionOverride>(_savedOverrides, StringComparer.Ordinal));
        _overrides
            .Setup(o => o.GetArchivedSessionIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _savedOverrides.Values
                .Where(sessionOverride => sessionOverride.IsArchived)
                .Select(sessionOverride => sessionOverride.SessionId)
                .ToHashSet(StringComparer.Ordinal));

        _repository = new OverridingSessionRepository(_inner, _overrides.Object);
        _search = new OverridingSessionSearch(_inner, _overrides.Object);
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
            CREATE TABLE session_files (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                session_id TEXT NOT NULL,
                file_path TEXT,
                tool_name TEXT,
                turn_index INTEGER,
                first_seen_at TEXT
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

            INSERT INTO turns (session_id, turn_index, user_message, assistant_response, timestamp) VALUES
                ('sess-1', 0, 'First', 'First response', '2025-01-01T10:01:00Z'),
                ('sess-1', 1, 'Second', 'Second response', '2025-01-01T10:02:00Z'),
                ('sess-2', 0, 'Third', 'Third response', '2025-01-03T09:01:00Z');

            INSERT INTO session_files (session_id, file_path, tool_name, turn_index, first_seen_at) VALUES
                ('sess-1', 'src/One.cs', 'edit', 0, '2025-01-01T10:01:00Z'),
                ('sess-1', 'src/Two.cs', 'create', 1, '2025-01-01T10:02:00Z'),
                ('sess-2', 'src/Three.cs', 'edit', 0, '2025-01-03T09:01:00Z');

            INSERT INTO session_refs (session_id, ref_type, ref_value, turn_index, created_at) VALUES
                ('sess-1', 'commit', 'abc123', 1, '2025-01-01T10:06:00Z');

            INSERT INTO search_index (content, session_id, source_type, source_id) VALUES
                ('Committed deadc0de1234567890 work done', 'sess-2', 'checkpoint_work_done', '1'),
                ('priority priority priority priority', 'sess-2', 'turn', '2'),
                ('priority appears among many unrelated words in this result', 'sess-1', 'turn', '3');
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
        _savedOverrides[ov.SessionId] = ov;

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
        _savedOverrides[ov.SessionId] = ov;

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
        _savedOverrides[ov.SessionId] = ov;

        var results = await _repository.GetSessionsByRefAsync("deadc0de", TestContext.Current.CancellationToken);

        Assert.Single(results);
        Assert.Equal("overridden/repo", results[0].Session.Repository);
        Assert.Equal(CommitMatchConfidence.Mentioned, results[0].Confidence);
    }

    [Fact]
    public async Task ListRecentAsync_ArchivedNewestSession_DoesNotConsumeLimit()
    {
        _savedOverrides["sess-2"] = MakeOverride("sess-2") with { IsArchived = true };

        var results = await _repository.ListRecentAsync(
            1,
            ct: TestContext.Current.CancellationToken);

        var session = Assert.Single(results);
        Assert.Equal("sess-1", session.Id);
    }

    [Fact]
    public async Task ListByRepositoryAsync_RepositoryOverride_UsesEffectiveRepository()
    {
        _savedOverrides["sess-1"] = MakeOverride("sess-1", repository: "custom/repo");

        var corrected = await _repository.ListByRepositoryAsync(
            "CUSTOM/REPO",
            ct: TestContext.Current.CancellationToken);
        var stale = await _repository.ListByRepositoryAsync(
            "owner/repo-a",
            ct: TestContext.Current.CancellationToken);

        Assert.Equal("sess-1", Assert.Single(corrected).Id);
        Assert.Empty(stale);
    }

    [Fact]
    public async Task ListByCwdAsync_TrailingSeparatorAndCaseDiffer_ReturnsSession()
    {
        var results = await _repository.ListByCwdAsync(
            @"c:\DEV\PROJ-A\",
            ct: TestContext.Current.CancellationToken);

        Assert.Equal("sess-1", Assert.Single(results).Id);
    }

    [Fact]
    public async Task GetRepositoryStatsAsync_RepositoryOverride_ReassignsAllSessionMetrics()
    {
        _savedOverrides["sess-1"] = MakeOverride("sess-1", repository: "custom/repo");

        var results = await _repository.GetRepositoryStatsAsync(TestContext.Current.CancellationToken);

        var corrected = Assert.Single(results, stats => stats.Repository == "custom/repo");
        Assert.Equal(1, corrected.SessionCount);
        Assert.Equal(2, corrected.TurnCount);
        Assert.Equal(2, corrected.FilesTouched);
        Assert.DoesNotContain(results, stats => stats.Repository == "owner/repo-a");
    }

    [Fact]
    public async Task GetRepositoryStatsAsync_ArchivedSession_ExcludesSessionFromTotals()
    {
        _savedOverrides["sess-2"] = MakeOverride("sess-2") with { IsArchived = true };

        var results = await _repository.GetRepositoryStatsAsync(TestContext.Current.CancellationToken);

        Assert.DoesNotContain(results, stats => stats.Repository == "owner/repo-b");
        Assert.Equal("owner/repo-a", Assert.Single(results).Repository);
    }

    [Fact]
    public async Task GetGlobalStatsAsync_UsesEffectiveMostActiveRepository()
    {
        _savedOverrides["sess-1"] = MakeOverride("sess-1", repository: "custom/repo");
        _savedOverrides["sess-2"] = MakeOverride("sess-2") with { IsArchived = true };

        var stats = await _repository.GetGlobalStatsAsync(TestContext.Current.CancellationToken);

        Assert.Equal("custom/repo", stats.MostActiveRepository);
    }

    [Fact]
    public async Task GetSessionInsightsAsync_UsesEffectiveVisibleRepositoriesAndBranches()
    {
        _savedOverrides["sess-1"] = MakeOverride(
            "sess-1",
            repository: "custom/repo",
            branch: "custom-branch");
        _savedOverrides["sess-2"] = MakeOverride("sess-2") with { IsArchived = true };

        var insights = await _repository.GetSessionInsightsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, insights.DistinctRepositories);
        Assert.Equal(1, insights.DistinctBranches);
    }

    [Fact]
    public async Task SearchAsync_ArchivedTopMatch_DoesNotConsumeLimit()
    {
        var raw = await _inner.SearchAsync(
            "priority",
            1,
            TestContext.Current.CancellationToken);
        Assert.Equal("sess-2", Assert.Single(raw).SessionId);
        _savedOverrides["sess-2"] = MakeOverride("sess-2") with { IsArchived = true };

        var results = await _search.SearchAsync(
            "priority",
            1,
            TestContext.Current.CancellationToken);

        Assert.Equal("sess-1", Assert.Single(results).SessionId);
    }

    [Fact]
    public async Task SearchAsync_IncludeArchived_ReturnsArchivedTopMatch()
    {
        _savedOverrides["sess-2"] = MakeOverride("sess-2") with { IsArchived = true };

        var results = await _search.SearchAsync(
            "priority",
            1,
            includeArchived: true,
            TestContext.Current.CancellationToken);

        Assert.Equal("sess-2", Assert.Single(results).SessionId);
    }

    private static SessionOverride MakeOverride(
        string sessionId,
        string? repository = null,
        string? branch = null) =>
        new(
            sessionId,
            DisplayName: null,
            Repository: repository,
            Branch: branch,
            Notes: null,
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow);
}
