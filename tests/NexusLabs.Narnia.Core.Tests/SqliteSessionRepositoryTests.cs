using Microsoft.Data.Sqlite;
using NexusLabs.Narnia.Core.Configuration;
using NexusLabs.Narnia.Core.Repositories;

namespace NexusLabs.Narnia.Core.Tests;

public sealed class SqliteSessionRepositoryTests : IDisposable
{
    private readonly SqliteConnection _keepAlive;
    private readonly SqliteSessionRepository _repository;

    public SqliteSessionRepositoryTests()
    {
        var dbName = $"narnia_test_{Guid.NewGuid():N}";
        var connectionString = $"Data Source={dbName};Mode=Memory;Cache=Shared";

        // Keep one connection open so the in-memory DB survives for the test lifetime
        _keepAlive = new SqliteConnection(connectionString);
        _keepAlive.Open();
        CreateSchema();
        SeedData();

        var options = new NarniaOptions
        {
            ConnectionString = connectionString,
        };
        _repository = new SqliteSessionRepository(options);
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
                updated_at TEXT
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
            INSERT INTO sessions VALUES
                ('sess-1', 'C:\dev\proj-a', 'owner/repo-a', 'main', 'Build the API', '2025-01-01T10:00:00Z', '2025-01-02T12:00:00Z'),
                ('sess-2', 'C:\dev\proj-b', 'owner/repo-b', 'feature/x', 'Fix the tests', '2025-01-03T09:00:00Z', '2025-01-03T11:00:00Z'),
                ('sess-3', 'C:\dev\proj-a', 'owner/repo-a', 'main', 'Add caching', '2025-01-04T08:00:00Z', '2025-01-04T09:00:00Z');

            INSERT INTO turns (session_id, turn_index, user_message, assistant_response, timestamp) VALUES
                ('sess-1', 0, 'Hello world', 'Hi there', '2025-01-01T10:01:00Z'),
                ('sess-1', 1, 'Build something', 'Done', '2025-01-01T10:05:00Z'),
                ('sess-2', 0, 'Fix tests', 'Fixed', '2025-01-03T09:01:00Z');

            INSERT INTO checkpoints (session_id, checkpoint_number, title, overview, history, work_done, technical_details, important_files, next_steps, created_at) VALUES
                ('sess-1', 1, 'First checkpoint', 'Overview text', 'History text', 'Work done text', 'Tech details', 'files.txt', 'Next steps', '2025-01-01T11:00:00Z');

            INSERT INTO session_files (session_id, file_path, tool_name, turn_index, first_seen_at) VALUES
                ('sess-1', 'src/Program.cs', 'edit', 0, '2025-01-01T10:02:00Z');

            INSERT INTO session_refs (session_id, ref_type, ref_value, turn_index, created_at) VALUES
                ('sess-1', 'commit', 'abc123', 1, '2025-01-01T10:06:00Z');

            INSERT INTO search_index (content, session_id, source_type, source_id) VALUES
                ('Build the API caching dependency injection', 'sess-1', 'turn', '1'),
                ('Fix the tests xunit mocking', 'sess-2', 'turn', '1');
            """;
        cmd.ExecuteNonQuery();
    }

    [Fact]
    public async Task ListRecentAsync_ReturnsSessionsOrderedByUpdatedAt()
    {
        var results = await _repository.ListRecentAsync(10, TestContext.Current.CancellationToken);

        Assert.Equal(3, results.Length);
        Assert.Equal("sess-3", results[0].Id);
        Assert.Equal("sess-2", results[1].Id);
        Assert.Equal("sess-1", results[2].Id);
    }

    [Fact]
    public async Task ListRecentAsync_RespectsLimit()
    {
        var results = await _repository.ListRecentAsync(2, TestContext.Current.CancellationToken);

        Assert.Equal(2, results.Length);
    }

    [Fact]
    public async Task ListRecentAsync_IncludesTurnAndCheckpointCounts()
    {
        var results = await _repository.ListRecentAsync(10, TestContext.Current.CancellationToken);
        var sess1 = Array.Find(results, s => s.Id == "sess-1");

        Assert.NotNull(sess1);
        Assert.Equal(2, sess1.TurnCount);
        Assert.Equal(1, sess1.CheckpointCount);
    }

    [Fact]
    public async Task ListByRepositoryAsync_FiltersByRepository()
    {
        var results = await _repository.ListByRepositoryAsync("owner/repo-a", TestContext.Current.CancellationToken);

        Assert.Equal(2, results.Length);
        Assert.All(results, s => Assert.Equal("owner/repo-a", s.Repository));
    }

    [Fact]
    public async Task ListByCwdAsync_FiltersByDirectory()
    {
        var results = await _repository.ListByCwdAsync(@"C:\dev\proj-a", TestContext.Current.CancellationToken);

        Assert.Equal(2, results.Length);
        Assert.All(results, s => Assert.Equal(@"C:\dev\proj-a", s.Cwd));
    }

    [Fact]
    public async Task GetByIdAsync_ExistingSession_ReturnsSession()
    {
        var session = await _repository.GetByIdAsync("sess-1", TestContext.Current.CancellationToken);

        Assert.NotNull(session);
        Assert.Equal("sess-1", session.Id);
        Assert.Equal("Build the API", session.Summary);
        Assert.Equal("owner/repo-a", session.Repository);
        Assert.Equal("main", session.Branch);
    }

    [Fact]
    public async Task GetByIdAsync_NonExistentSession_ReturnsNull()
    {
        var session = await _repository.GetByIdAsync("does-not-exist", TestContext.Current.CancellationToken);

        Assert.Null(session);
    }

    [Fact]
    public async Task GetTurnsAsync_ReturnsOrderedTurns()
    {
        var turns = await _repository.GetTurnsAsync("sess-1", 0, 10, TestContext.Current.CancellationToken);

        Assert.Equal(2, turns.Length);
        Assert.Equal(0, turns[0].TurnIndex);
        Assert.Equal(1, turns[1].TurnIndex);
    }

    [Fact]
    public async Task GetTurnsAsync_RespectsPagination()
    {
        var turns = await _repository.GetTurnsAsync("sess-1", 1, 10, TestContext.Current.CancellationToken);

        Assert.Single(turns);
        Assert.Equal(1, turns[0].TurnIndex);
    }

    [Fact]
    public async Task GetCheckpointsAsync_ReturnsCheckpoints()
    {
        var checkpoints = await _repository.GetCheckpointsAsync("sess-1", TestContext.Current.CancellationToken);

        Assert.Single(checkpoints);
        Assert.Equal("First checkpoint", checkpoints[0].Title);
        Assert.Equal("Overview text", checkpoints[0].Overview);
    }

    [Fact]
    public async Task GetFilesAsync_ReturnsFiles()
    {
        var files = await _repository.GetFilesAsync("sess-1", TestContext.Current.CancellationToken);

        Assert.Single(files);
        Assert.Equal("src/Program.cs", files[0].FilePath);
    }

    [Fact]
    public async Task GetRefsAsync_ReturnsRefs()
    {
        var refs = await _repository.GetRefsAsync("sess-1", TestContext.Current.CancellationToken);

        Assert.Single(refs);
        Assert.Equal("commit", refs[0].RefType);
        Assert.Equal("abc123", refs[0].RefValue);
    }

    [Fact]
    public async Task SearchAsync_MatchesContent()
    {
        var results = await _repository.SearchAsync("caching", 10, TestContext.Current.CancellationToken);

        Assert.Single(results);
        Assert.Equal("sess-1", results[0].SessionId);
    }

    [Fact]
    public async Task SearchAsync_QueryWithSpecialChars_DoesNotThrow()
    {
        // Slash and other FTS5 special chars must not cause a syntax error.
        var ex = await Record.ExceptionAsync(() =>
            _repository.SearchAsync("ncosentino/devleader-blog", 10, TestContext.Current.CancellationToken).AsTask());

        Assert.Null(ex);
    }

    // ── GetGlobalStatsAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task GetGlobalStatsAsync_ReturnsTotalSessions()
    {
        var stats = await _repository.GetGlobalStatsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(3, stats.TotalSessions);
    }

    [Fact]
    public async Task GetGlobalStatsAsync_ReturnsTotalTurns()
    {
        var stats = await _repository.GetGlobalStatsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(3, stats.TotalTurns);
    }

    [Fact]
    public async Task GetGlobalStatsAsync_ReturnsAvgTurnsPerSession()
    {
        var stats = await _repository.GetGlobalStatsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1.0, stats.AvgTurnsPerSession);
    }

    [Fact]
    public async Task GetGlobalStatsAsync_ReturnsTotalFilesTouched()
    {
        var stats = await _repository.GetGlobalStatsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, stats.TotalFilesTouched);
    }

    [Fact]
    public async Task GetGlobalStatsAsync_ReturnsMostActiveRepository()
    {
        var stats = await _repository.GetGlobalStatsAsync(TestContext.Current.CancellationToken);

        Assert.Equal("owner/repo-a", stats.MostActiveRepository);
    }

    [Fact]
    public async Task GetGlobalStatsAsync_ReturnsBusiestDay()
    {
        var stats = await _repository.GetGlobalStatsAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(stats.BusiestDay);
    }

    // ── GetActivityByDateAsync ────────────────────────────────────────────────

    [Fact]
    public async Task GetActivityByDateAsync_ReturnsSessionsWithinWindow()
    {
        var results = await _repository.GetActivityByDateAsync(9999, TestContext.Current.CancellationToken);

        Assert.True(results.Length >= 3);
    }

    [Fact]
    public async Task GetActivityByDateAsync_ResultsAreSortedByDate()
    {
        var results = await _repository.GetActivityByDateAsync(9999, TestContext.Current.CancellationToken);

        for (var i = 1; i < results.Length; i++)
            Assert.True(results[i].Date >= results[i - 1].Date);
    }

    [Fact]
    public async Task GetActivityByDateAsync_CountsMatchSeedData()
    {
        var results = await _repository.GetActivityByDateAsync(9999, TestContext.Current.CancellationToken);
        var total = results.Sum(r => r.SessionCount);

        Assert.Equal(3, total);
    }

    // ── GetRepositoryStatsAsync ───────────────────────────────────────────────

    [Fact]
    public async Task GetRepositoryStatsAsync_ReturnsOneRowPerRepository()
    {
        var results = await _repository.GetRepositoryStatsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, results.Length);
    }

    [Fact]
    public async Task GetRepositoryStatsAsync_CountsSessionsPerRepository()
    {
        var results = await _repository.GetRepositoryStatsAsync(TestContext.Current.CancellationToken);
        var repoA = Array.Find(results, r => r.Repository == "owner/repo-a");

        Assert.NotNull(repoA);
        Assert.Equal(2, repoA.SessionCount);
    }

    [Fact]
    public async Task GetRepositoryStatsAsync_OrderedBySessionCountDescending()
    {
        var results = await _repository.GetRepositoryStatsAsync(TestContext.Current.CancellationToken);

        for (var i = 1; i < results.Length; i++)
            Assert.True(results[i].SessionCount <= results[i - 1].SessionCount);
    }

    // ── GetHotFilesAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetHotFilesAsync_ReturnsFiles()
    {
        var results = await _repository.GetHotFilesAsync(10, TestContext.Current.CancellationToken);

        Assert.Single(results);
        Assert.Equal("src/Program.cs", results[0].FilePath);
    }

    [Fact]
    public async Task GetHotFilesAsync_RespectsLimit()
    {
        var results = await _repository.GetHotFilesAsync(0, TestContext.Current.CancellationToken);

        Assert.Empty(results);
    }

    [Fact]
    public async Task GetHotFilesAsync_ReturnsToolName()
    {
        var results = await _repository.GetHotFilesAsync(10, TestContext.Current.CancellationToken);

        Assert.Equal("edit", results[0].LastToolName);
    }

    // ── GetFileHistoryAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task GetFileHistoryAsync_ReturnsSessionsThatTouchedFile()
    {
        var results = await _repository.GetFileHistoryAsync("src/Program.cs", TestContext.Current.CancellationToken);

        Assert.Single(results);
        Assert.Equal("sess-1", results[0].SessionId);
    }

    [Fact]
    public async Task GetFileHistoryAsync_UnknownFile_ReturnsEmpty()
    {
        var results = await _repository.GetFileHistoryAsync("nonexistent/file.cs", TestContext.Current.CancellationToken);

        Assert.Empty(results);
    }

    [Fact]
    public async Task GetFileHistoryAsync_IncludesCheckpointOverview()
    {
        var results = await _repository.GetFileHistoryAsync("src/Program.cs", TestContext.Current.CancellationToken);

        Assert.Equal("Overview text", results[0].CheckpointOverview);
    }

    // ── GetSessionsByRefAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task GetSessionsByRefAsync_MatchesExistingRef()
    {
        var results = await _repository.GetSessionsByRefAsync("abc123", TestContext.Current.CancellationToken);

        Assert.Single(results);
        Assert.Equal("sess-1", results[0].Id);
    }

    [Fact]
    public async Task GetSessionsByRefAsync_UnknownRef_ReturnsEmpty()
    {
        var results = await _repository.GetSessionsByRefAsync("deadbeef", TestContext.Current.CancellationToken);

        Assert.Empty(results);
    }

    // ── GetResumeSuggestionsAsync ─────────────────────────────────────────────

    [Fact]
    public async Task GetResumeSuggestionsAsync_ReturnsSessionsWithNextSteps()
    {
        var results = await _repository.GetResumeSuggestionsAsync(10, TestContext.Current.CancellationToken);

        Assert.Single(results);
        Assert.Equal("sess-1", results[0].Session.Id);
    }

    [Fact]
    public async Task GetResumeSuggestionsAsync_IncludesNextStepsPreview()
    {
        var results = await _repository.GetResumeSuggestionsAsync(10, TestContext.Current.CancellationToken);

        Assert.Contains("Next steps", results[0].NextStepsPreview);
    }

    [Fact]
    public async Task GetResumeSuggestionsAsync_RespectsLimit()
    {
        var results = await _repository.GetResumeSuggestionsAsync(0, TestContext.Current.CancellationToken);

        Assert.Empty(results);
    }

    // ── GetTopKeywordsAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task GetTopKeywordsAsync_ReturnsKeywords()
    {
        var results = await _repository.GetTopKeywordsAsync(50, TestContext.Current.CancellationToken);

        Assert.NotEmpty(results);
    }

    [Fact]
    public async Task GetTopKeywordsAsync_RespectsTopN()
    {
        var results = await _repository.GetTopKeywordsAsync(2, TestContext.Current.CancellationToken);

        Assert.True(results.Length <= 2);
    }

    [Fact]
    public async Task GetTopKeywordsAsync_KeywordsAreOrderedByCountDescending()
    {
        var results = await _repository.GetTopKeywordsAsync(50, TestContext.Current.CancellationToken);

        for (var i = 1; i < results.Length; i++)
            Assert.True(results[i].Count <= results[i - 1].Count);
    }

    [Fact]
    public async Task GetTopKeywordsAsync_ExcludesStopWords()
    {
        var results = await _repository.GetTopKeywordsAsync(50, TestContext.Current.CancellationToken);
        var keywords = results.Select(r => r.Keyword).ToArray();

        Assert.DoesNotContain("the", keywords);
        Assert.DoesNotContain("and", keywords);
    }
}
