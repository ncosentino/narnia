using Microsoft.Data.Sqlite;
using NexusLabs.Narnia.Core.Configuration;
using NexusLabs.Narnia.Core.Models;
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

    private async Task ExecuteAsync(string sql)
    {
        await using var cmd = _keepAlive.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private void SeedData()
    {
        using var cmd = _keepAlive.CreateCommand();
        cmd.CommandText = """
            INSERT INTO sessions (id, cwd, repository, branch, summary, created_at, updated_at) VALUES
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
                ('Fix the tests xunit mocking', 'sess-2', 'turn', '1'),
                ('Committed deadc0de1234567890abcdef1234567890deadc0de work done', 'sess-2', 'checkpoint_work_done', '2'),
                ('widget widget widget alpha', 'sess-2', 'turn', '10'),
                ('widget widget widget beta', 'sess-2', 'turn', '11'),
                ('widget widget widget gamma', 'sess-2', 'turn', '12'),
                ('widget widget widget delta', 'sess-2', 'turn', '13'),
                ('widget widget widget epsilon', 'sess-2', 'turn', '14'),
                ('the team briefly mentioned a widget among many other agenda items', 'sess-3', 'turn', '1');
            """;
        cmd.ExecuteNonQuery();
    }

    [Fact]
    public async Task ListAllAsync_ReturnsEverySessionOrderedByUpdatedAt()
    {
        var results = await _repository.ListAllAsync(ct: TestContext.Current.CancellationToken);

        Assert.Equal(new[] { "sess-3", "sess-2", "sess-1" }, results.Select(session => session.Id));
    }

    [Fact]
    public async Task ListAllAsync_EqualUpdatedAt_UsesSessionIdTieBreaker()
    {
        await ExecuteAsync(
            """
            INSERT INTO sessions (id, cwd, repository, branch, summary, created_at, updated_at) VALUES
                ('tie-b', 'C:\dev\tie', 'owner/tie', 'main', 'Tie B', '2025-02-01T00:00:00Z', '2025-02-02T00:00:00Z'),
                ('tie-a', 'C:\dev\tie', 'owner/tie', 'main', 'Tie A', '2025-02-01T00:00:00Z', '2025-02-02T00:00:00Z');
            """);

        var results = await _repository.ListAllAsync(ct: TestContext.Current.CancellationToken);

        Assert.Equal(new[] { "tie-a", "tie-b" }, results.Take(2).Select(session => session.Id));
    }

    [Fact]
    public async Task ListRecentAsync_ReturnsSessionsOrderedByUpdatedAt()
    {
        var results = await _repository.ListRecentAsync(10, ct: TestContext.Current.CancellationToken);

        Assert.Equal(3, results.Length);
        Assert.Equal("sess-3", results[0].Id);
        Assert.Equal("sess-2", results[1].Id);
        Assert.Equal("sess-1", results[2].Id);
    }

    [Fact]
    public async Task ListRecentAsync_RespectsLimit()
    {
        var results = await _repository.ListRecentAsync(2, ct: TestContext.Current.CancellationToken);

        Assert.Equal(2, results.Length);
    }

    [Fact]
    public async Task ListRecentAsync_IncludesTurnAndCheckpointCounts()
    {
        var results = await _repository.ListRecentAsync(10, ct: TestContext.Current.CancellationToken);
        var sess1 = Array.Find(results, s => s.Id == "sess-1");

        Assert.NotNull(sess1);
        Assert.Equal(2, sess1.TurnCount);
        Assert.Equal(1, sess1.CheckpointCount);
    }

    [Fact]
    public async Task ListByRepositoryAsync_FiltersByRepository()
    {
        var results = await _repository.ListByRepositoryAsync("owner/repo-a", ct: TestContext.Current.CancellationToken);

        Assert.Equal(2, results.Length);
        Assert.All(results, s => Assert.Equal("owner/repo-a", s.Repository));
    }

    [Fact]
    public async Task ListByCwdAsync_FiltersByDirectory()
    {
        var results = await _repository.ListByCwdAsync(@"C:\dev\proj-a", ct: TestContext.Current.CancellationToken);

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
    public async Task GetByIdsAsync_ReturnsExistingSessionsKeyedById()
    {
        var sessions = await _repository.GetByIdsAsync(
            ["sess-2", "does-not-exist", "sess-1", "sess-2", " "],
            TestContext.Current.CancellationToken);

        Assert.Equal(2, sessions.Count);
        Assert.Equal("Build the API", sessions["sess-1"].Summary);
        Assert.Equal("Fix the tests", sessions["sess-2"].Summary);
        Assert.False(sessions.ContainsKey("does-not-exist"));
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

    [Fact]
    public async Task SearchAsync_QueryWithoutSearchableCharacters_ReturnsEmpty()
    {
        var results = await _repository.SearchAsync(
            "///",
            10,
            TestContext.Current.CancellationToken);

        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchAsync_WildcardQuery_ReturnsResults()
    {
        // "cach*" should prefix-match "caching" in the seed data.
        var results = await _repository.SearchAsync("cach*", 10, TestContext.Current.CancellationToken);

        Assert.NotEmpty(results);
        Assert.Equal("sess-1", results[0].SessionId);
    }

    [Fact]
    public async Task SearchAsync_ChattySessionDoesNotCrowdOutOtherMatchingSessions()
    {
        // sess-2 has 5 strongly-matching "widget" rows; sess-3 has only 1, weaker match.
        // A low limit must still surface both sessions instead of exhausting the limit
        // on sess-2's many rows alone.
        var results = await _repository.SearchAsync("widget", 2, TestContext.Current.CancellationToken);

        var sessionIds = results.Select(r => r.SessionId).ToArray();
        Assert.Equal(2, sessionIds.Length);
        Assert.Equal(sessionIds.Length, sessionIds.Distinct().Count());
        Assert.Contains("sess-2", sessionIds);
        Assert.Contains("sess-3", sessionIds);
    }

    [Fact]
    public async Task SearchAsync_ReturnsAtMostOneRowPerSession()
    {
        // sess-2 alone has 5 matching "widget" rows. A limit of 1 must return a single
        // result, not the top raw FTS row (which could still be one of sess-2's five).
        var results = await _repository.SearchAsync("widget", 1, TestContext.Current.CancellationToken);

        Assert.Single(results);
    }

    [Fact]
    public async Task SearchAsync_EqualRanks_UsesSessionIdTieBreaker()
    {
        await ExecuteAsync(
            """
            INSERT INTO sessions (id, cwd, repository, branch, summary, created_at, updated_at) VALUES
                ('search-b', 'C:\dev\search', 'owner/search', 'main', 'Search B', '2025-02-01T00:00:00Z', '2025-02-02T00:00:00Z'),
                ('search-a', 'C:\dev\search', 'owner/search', 'main', 'Search A', '2025-02-01T00:00:00Z', '2025-02-02T00:00:00Z');
            INSERT INTO search_index (content, session_id, source_type, source_id) VALUES
                ('deterministic tie content', 'search-b', 'turn', '1'),
                ('deterministic tie content', 'search-a', 'turn', '1');
            """);

        var results = await _repository.SearchAsync(
            "deterministic",
            10,
            TestContext.Current.CancellationToken);

        Assert.Equal(new[] { "search-a", "search-b" }, results.Select(result => result.SessionId));
    }

    [Fact]
    public async Task GetSessionsByRefAsync_ExactMatch_ReturnsSession()
    {
        var results = await _repository.GetSessionsByRefAsync("abc123", TestContext.Current.CancellationToken);

        Assert.Single(results);
        Assert.Equal("sess-1", results[0].Session.Id);
    }

    [Fact]
    public async Task GetSessionsByRefAsync_ExactMatch_IsConfirmed()
    {
        // "abc123" is an explicit session_refs row for sess-1, not merely a text mention.
        var results = await _repository.GetSessionsByRefAsync("abc123", TestContext.Current.CancellationToken);

        Assert.Single(results);
        Assert.Equal(CommitMatchConfidence.Confirmed, results[0].Confidence);
    }

    [Fact]
    public async Task GetSessionsByRefAsync_PrefixOfStoredRef_ReturnsSession()
    {
        // User types a short prefix (at the 4-char validation floor) of a longer stored SHA.
        var results = await _repository.GetSessionsByRefAsync("abc1", TestContext.Current.CancellationToken);

        Assert.Single(results);
        Assert.Equal("sess-1", results[0].Session.Id);
    }

    [Fact]
    public async Task GetSessionsByRefAsync_StoredRefIsPrefixOfQuery_ReturnsSession()
    {
        // Stored SHA is short ("abc123"), user types the full expanded version.
        var results = await _repository.GetSessionsByRefAsync("abc123def456", TestContext.Current.CancellationToken);

        Assert.Single(results);
        Assert.Equal("sess-1", results[0].Session.Id);
    }

    [Fact]
    public async Task GetSessionsByRefAsync_UnknownRef_ReturnsEmpty()
    {
        var results = await _repository.GetSessionsByRefAsync("deadbeef", TestContext.Current.CancellationToken);

        Assert.Empty(results);
    }

    [Fact]
    public async Task GetSessionsByRefAsync_LongInputMatchesShortShaInContent_ReturnsSession()
    {
        // User types full 40-char SHA; content only mentions the short 8-char prefix.
        // "deadc0de" appears in search_index for sess-2.
        var results = await _repository.GetSessionsByRefAsync("deadc0de1234567890abcdef1234567890000000", TestContext.Current.CancellationToken);

        Assert.Single(results);
        Assert.Equal("sess-2", results[0].Session.Id);
    }

    [Fact]
    public async Task GetSessionsByRefAsync_ShaInSessionContent_ReturnsSession()
    {
        // SHA mentioned in checkpoint text (FTS fallback path), not in session_refs. Uses the
        // first 40 (max valid SHA-1 length) characters of the seeded checkpoint content's token.
        var results = await _repository.GetSessionsByRefAsync("deadc0de1234567890abcdef1234567890deadc0", TestContext.Current.CancellationToken);

        Assert.Single(results);
        Assert.Equal("sess-2", results[0].Session.Id);
    }

    [Fact]
    public async Task GetSessionsByRefAsync_ShortShaInSessionContent_ReturnsSession()
    {
        // Short prefix of a SHA that appears in session text.
        var results = await _repository.GetSessionsByRefAsync("deadc0de", TestContext.Current.CancellationToken);

        Assert.Single(results);
        Assert.Equal("sess-2", results[0].Session.Id);
    }

    [Fact]
    public async Task GetSessionsByRefAsync_TextOnlyMatch_IsMentioned()
    {
        // "deadc0de..." only appears in search_index content for sess-2, never in session_refs.
        var results = await _repository.GetSessionsByRefAsync("deadc0de", TestContext.Current.CancellationToken);

        Assert.Single(results);
        Assert.Equal(CommitMatchConfidence.Mentioned, results[0].Confidence);
    }

    [Fact]
    public async Task GetSessionsByRefAsync_UppercaseInput_NormalizedAndStillMatches()
    {
        // A SHA pasted with uppercase letters (e.g. from a tool that displays them that way)
        // must still match the lowercase-stored ref.
        var results = await _repository.GetSessionsByRefAsync("ABC123", TestContext.Current.CancellationToken);

        Assert.Single(results);
        Assert.Equal("sess-1", results[0].Session.Id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a")]
    [InlineData("ab")]
    [InlineData("abc")]
    public async Task GetSessionsByRefAsync_TooShortInput_ReturnsEmptyWithoutFlooding(string value)
    {
        // Below the 4-char validation floor, a query must be rejected rather than returning
        // a flood of unrelated sessions (a 1-2 char hex prefix can match most of the table).
        var results = await _repository.GetSessionsByRefAsync(value, TestContext.Current.CancellationToken);

        Assert.Empty(results);
    }

    [Theory]
    [InlineData("abc\"def")]
    [InlineData("abc:def")]
    [InlineData("abc)(def")]
    [InlineData("NOT abcd")]
    [InlineData("abcd OR wxyz")]
    [InlineData("*")]
    [InlineData("zzzznothex")]
    public async Task GetSessionsByRefAsync_MalformedOrNonHexInput_DoesNotThrowAndReturnsEmpty(string value)
    {
        // These would previously reach the FTS5 MATCH parameter unescaped and either throw
        // a syntax error (quotes, colons, parens, "NOT"/"OR") or -- for the non-hex case --
        // simply never legitimately match a SHA. Validation must reject all of them cleanly.
        var results = await _repository.GetSessionsByRefAsync(value, TestContext.Current.CancellationToken);

        Assert.Empty(results);
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

    // ── GetActivityTimelineAsync ──────────────────────────────────────────────

    [Fact]
    public async Task GetActivityTimelineAsync_ReturnsEveryTrackedActivityTypeByDate()
    {
        var results = await _repository.GetActivityTimelineAsync(9999, TestContext.Current.CancellationToken);

        var firstDay = Assert.Single(results, day => day.Date == new DateOnly(2025, 1, 1));
        Assert.Equal(1, firstDay.SessionCount);
        Assert.Equal(2, firstDay.TurnCount);
        Assert.Equal(1, firstDay.FilesTouched);
        Assert.Equal(1, firstDay.CheckpointCount);

        Assert.Equal(3, results.Sum(day => day.SessionCount));
        Assert.Equal(3, results.Sum(day => day.TurnCount));
        Assert.Equal(1, results.Sum(day => day.FilesTouched));
        Assert.Equal(1, results.Sum(day => day.CheckpointCount));
    }

    [Fact]
    public async Task GetActivityTimelineAsync_UsesEventDateAndFallsBackToSessionDate()
    {
        await ExecuteAsync(
            """
            INSERT INTO turns (session_id, turn_index, user_message, assistant_response, timestamp)
            VALUES ('sess-1', 2, 'Later question', 'Later answer', '2025-01-05T10:00:00Z');

            INSERT INTO session_files (session_id, file_path, tool_name, turn_index, first_seen_at)
            VALUES ('sess-2', 'src/Fallback.cs', 'edit', 1, NULL);
            """);

        var results = await _repository.GetActivityTimelineAsync(9999, TestContext.Current.CancellationToken);

        var laterTurnDay = Assert.Single(results, day => day.Date == new DateOnly(2025, 1, 5));
        Assert.Equal(0, laterTurnDay.SessionCount);
        Assert.Equal(1, laterTurnDay.TurnCount);

        var fallbackDay = Assert.Single(results, day => day.Date == new DateOnly(2025, 1, 3));
        Assert.Equal(1, fallbackDay.FilesTouched);
    }

    [Fact]
    public async Task GetActivityTimelineAsync_ResultsAreSortedByDate()
    {
        var results = await _repository.GetActivityTimelineAsync(9999, TestContext.Current.CancellationToken);

        for (var i = 1; i < results.Length; i++)
            Assert.True(results[i].Date >= results[i - 1].Date);
    }

    [Fact]
    public async Task GetSessionActivitySourcesAsync_CollapsesGeneratedWorkingDirectories()
    {
        var now = DateTimeOffset.Now;
        var date = DateOnly.FromDateTime(now.Date);
        await ExecuteAsync(
            $"""
            INSERT INTO sessions (id, cwd, repository, branch, summary, created_at, updated_at) VALUES
                ('eval-1', 'C:\Temp\bg-eval-judge\10873c94704f4fcea064cda3049c6251', NULL, NULL, 'Eval one', '{now:O}', '{now:O}'),
                ('eval-2', 'C:\Temp\bg-eval-judge\4a636c27fc9a455aa29b53dc8d3089c6', NULL, NULL, 'Eval two', '{now:O}', '{now:O}'),
                ('exact-trailing', 'C:\dev\exact\', NULL, NULL, 'Exact directory', '{now:O}', '{now:O}'),
                ('repo-source', 'C:\dev\sample', 'owner/sample', 'main', 'Repository session', '{now:O}', '{now:O}');
            """);

        var results = await _repository.GetSessionActivitySourcesAsync(
            date,
            TestContext.Current.CancellationToken);

        var eval = Assert.Single(
            results,
            source => source.WorkingDirectory == @"C:\Temp\bg-eval-judge");
        Assert.Equal(2, eval.SessionCount);
        Assert.True(eval.IncludesDescendants);
        Assert.Equal(@"C:\Temp\bg-eval-judge\*", eval.Label);

        var repository = Assert.Single(
            results,
            source => source.Repository == "owner/sample");
        Assert.Equal(1, repository.SessionCount);
        Assert.Equal(SessionActivitySourceKind.RemoteRepository, repository.Kind);

        var sessions = await _repository.ListByActivitySourceAsync(
            new SessionActivitySourceFilter(
                date,
                SessionActivitySourceKind.WorkingDirectory,
                null,
                @"C:\Temp\bg-eval-judge",
                true,
                null,
                true),
            includeArchived: true,
            ct: TestContext.Current.CancellationToken);
        Assert.Equal(2, sessions.Length);
        Assert.All(
            sessions,
            session => Assert.StartsWith(
                @"C:\Temp\bg-eval-judge\",
                session.Cwd,
                StringComparison.OrdinalIgnoreCase));

        var exactSessions = await _repository.ListByActivitySourceAsync(
            new SessionActivitySourceFilter(
                date,
                SessionActivitySourceKind.WorkingDirectory,
                null,
                @"C:\dev\exact",
                false,
                null,
                true),
            includeArchived: true,
            ct: TestContext.Current.CancellationToken);
        Assert.Equal("exact-trailing", Assert.Single(exactSessions).Id);
    }

    [Fact]
    public async Task GetActivityTimelineAsync_UsesMachineLocalDate()
    {
        var now = DateTimeOffset.Now;
        var expectedDate = DateOnly.FromDateTime(now.Date);
        await ExecuteAsync(
            $"""
            INSERT INTO sessions (id, cwd, repository, branch, summary, created_at, updated_at)
            VALUES ('local-date', 'C:\dev\local', NULL, NULL, 'Local date', '{now.ToUniversalTime():O}', '{now.ToUniversalTime():O}');
            """);

        var results = await _repository.GetActivityTimelineAsync(
            1,
            TestContext.Current.CancellationToken);

        Assert.Contains(results, day => day.Date == expectedDate);
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

    // ── GetSessionInsightsAsync ───────────────────────────────────────────────

    [Fact]
    public async Task GetSessionInsightsAsync_ReturnsDistinctRepositoryAndBranchCounts()
    {
        var insights = await _repository.GetSessionInsightsAsync(TestContext.Current.CancellationToken);

        // Seed data has 2 repos (owner/repo-a, owner/repo-b) and 2 branches (main, feature/x)
        Assert.Equal(2, insights.DistinctRepositories);
        Assert.Equal(2, insights.DistinctBranches);
    }

    [Fact]
    public async Task GetSessionInsightsAsync_ReturnsTotalCheckpoints()
    {
        var insights = await _repository.GetSessionInsightsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, insights.TotalCheckpoints);
    }

    [Fact]
    public async Task GetSessionInsightsAsync_ReturnsFileOperationCounts()
    {
        await using (var cmd = _keepAlive.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO session_files (session_id, file_path, tool_name, turn_index, first_seen_at) VALUES
                    ('sess-1', 'src/New.cs', 'create', 2, '2025-01-01T10:07:00Z'),
                    ('sess-2', 'src/Other.cs', 'create', 1, '2025-01-03T09:05:00Z');
                """;
            await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        var insights = await _repository.GetSessionInsightsAsync(TestContext.Current.CancellationToken);

        // Seed data has 1 pre-existing 'edit' row; this test adds 2 more 'create' rows
        Assert.Equal(2, insights.FilesCreated);
        Assert.Equal(1, insights.FilesEdited);
    }

    [Fact]
    public async Task GetSessionInsightsAsync_ReturnsHostTypeCounts()
    {
        await using (var cmd = _keepAlive.CreateCommand())
        {
            cmd.CommandText = "UPDATE sessions SET host_type = 'github' WHERE id = 'sess-1'";
            await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        var insights = await _repository.GetSessionInsightsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, insights.GithubHostedSessions);
        Assert.Equal(2, insights.LocalTerminalSessions);
    }

    // ── GetActivityPatternsAsync ──────────────────────────────────────────────

    [Fact]
    public async Task GetActivityPatternsAsync_ByHour_ReturnsAllTwentyFourHoursZeroFilled()
    {
        var patterns = await _repository.GetActivityPatternsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(24, patterns.ByHour.Count);
        Assert.Equal(Enumerable.Range(0, 24), patterns.ByHour.Select(h => h.Hour));
    }

    [Fact]
    public async Task GetActivityPatternsAsync_ByDayOfWeek_ReturnsAllSevenDaysZeroFilled()
    {
        var patterns = await _repository.GetActivityPatternsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(7, patterns.ByDayOfWeek.Count);
    }

    [Fact]
    public async Task GetActivityPatternsAsync_ByHour_SumsToTotalSessions()
    {
        var patterns = await _repository.GetActivityPatternsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(3, patterns.ByHour.Sum(h => h.SessionCount));
    }

    [Fact]
    public async Task GetActivityPatternsAsync_ConvertsUtcTimestampsToLocalTime()
    {
        // sess-1 was seeded at 2025-01-01T10:00:00Z. Compute the expected bucket via the
        // same UTC -> local conversion rather than hardcoding an hour, since the raw UTC
        // hour would land in a different local bucket depending on the machine running
        // the test.
        var expectedHour = new DateTimeOffset(2025, 1, 1, 10, 0, 0, TimeSpan.Zero).ToLocalTime().Hour;

        var patterns = await _repository.GetActivityPatternsAsync(TestContext.Current.CancellationToken);

        Assert.True(patterns.ByHour.Single(h => h.Hour == expectedHour).SessionCount >= 1);
    }

    [Fact]
    public async Task GetActivityPatternsAsync_NoRecentActivity_CurrentStreakIsZero()
    {
        // Seed data is all from January 2025 — far in the past relative to whenever this
        // test runs — so there should be no ongoing streak.
        var patterns = await _repository.GetActivityPatternsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, patterns.CurrentStreakDays);
    }

    [Fact]
    public async Task GetActivityPatternsAsync_ComputesLongestAndCurrentStreak()
    {
        // Anchored to "now" and spaced exactly 24h apart so the 3 rows land on 3
        // consecutive local calendar days regardless of the runner's time zone.
        var now = DateTimeOffset.UtcNow;
        await using (var cmd = _keepAlive.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO sessions (id, cwd, repository, branch, summary, created_at, updated_at) VALUES
                    ('streak-1', 'C:\dev\streak', 'owner/streak', 'main', 'day 0', @d0, @d0),
                    ('streak-2', 'C:\dev\streak', 'owner/streak', 'main', 'day 1', @d1, @d1),
                    ('streak-3', 'C:\dev\streak', 'owner/streak', 'main', 'day 2', @d2, @d2);
                """;
            cmd.Parameters.AddWithValue("@d0", now.ToString("o"));
            cmd.Parameters.AddWithValue("@d1", now.AddDays(-1).ToString("o"));
            cmd.Parameters.AddWithValue("@d2", now.AddDays(-2).ToString("o"));
            await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        var patterns = await _repository.GetActivityPatternsAsync(TestContext.Current.CancellationToken);

        // The Jan-2025 seed data's longest possible run is 2 days (Jan 3 + Jan 4), so this
        // freshly-injected 3-day run becomes the new longest as well as the current streak.
        Assert.Equal(3, patterns.CurrentStreakDays);
        Assert.Equal(3, patterns.LongestStreakDays);
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
        await ExecuteAsync(
            """
            INSERT INTO session_files (session_id, file_path, tool_name, turn_index, first_seen_at)
            VALUES ('sess-2', 'src/Program.cs', 'create', 1, '2025-02-01T10:00:00Z');
            """);

        var results = await _repository.GetHotFilesAsync(10, TestContext.Current.CancellationToken);

        Assert.Equal("create", results[0].LastToolName);
    }

    [Fact]
    public async Task GetFileHotspotsAsync_AddsProjectAndTemporaryContext()
    {
        var temporaryFile = Path.Combine(
            Path.GetTempPath(),
            "eval-run",
            "app",
            "IMPLEMENTATION_PLAN.md");
        await using (var cmd = _keepAlive.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO session_files (session_id, file_path, tool_name, turn_index, first_seen_at) VALUES
                    ('sess-1', 'C:\dev\proj-a\src\Feature.cs', 'edit', 2, '2025-02-01T10:00:00Z'),
                    ('sess-2', @temporaryFile, 'create', 1, '2025-02-01T10:01:00Z'),
                    ('sess-1', 'C:\shared\global.json', 'edit', 3, '2025-02-01T10:02:00Z');
                """;
            cmd.Parameters.AddWithValue("@temporaryFile", temporaryFile);
            await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        var summary = await _repository.GetFileHotspotsAsync(
            20,
            TestContext.Current.CancellationToken);
        var results = summary.ProjectFiles.Concat(summary.Artifacts).ToArray();

        var project = Assert.Single(
            results,
            file => file.FilePath == @"C:\dev\proj-a\src\Feature.cs");
        Assert.Equal(FileActivityKind.Project, project.ActivityKind);
        Assert.Equal("owner/repo-a", project.Context);
        Assert.Equal(@"src\Feature.cs", project.DisplayPath);

        var temporary = Assert.Single(
            results,
            file => file.FilePath == temporaryFile);
        Assert.Equal(FileActivityKind.Temporary, temporary.ActivityKind);
        Assert.Equal("Temporary · eval-run", temporary.Context);
        Assert.Equal(@"app\IMPLEMENTATION_PLAN.md", temporary.DisplayPath);

        var external = Assert.Single(
            results,
            file => file.FilePath == @"C:\shared\global.json");
        Assert.Equal(FileActivityKind.Other, external.ActivityKind);
    }

    [Fact]
    public async Task GetFileHotspotsAsync_UsesContextAwareCanonicalIdentity()
    {
        await ExecuteAsync(
            """
            INSERT INTO session_files (session_id, file_path, tool_name, turn_index, first_seen_at) VALUES
                ('sess-1', 'C:\dev\proj-a\src\Shared.cs', 'edit', 2, '2025-02-01T10:00:00Z'),
                ('sess-2', 'c:/DEV/proj-a/src/Shared.cs', 'edit', 1, '2025-02-01T10:01:00Z'),
                ('sess-1', 'src\Relative.cs', 'edit', 3, '2025-02-01T10:02:00Z'),
                ('sess-2', 'src\Relative.cs', 'edit', 2, '2025-02-01T10:03:00Z'),
                ('sess-1', 'C:src\DriveRelative.cs', 'edit', 4, '2025-02-01T10:04:00Z');
            """);

        var summary = await _repository.GetFileHotspotsAsync(
            50,
            TestContext.Current.CancellationToken);
        var results = summary.ProjectFiles.Concat(summary.Artifacts).ToArray();

        var shared = Assert.Single(
            results,
            file => file.FilePath.Equals(
                @"C:\dev\proj-a\src\Shared.cs",
                StringComparison.OrdinalIgnoreCase));
        Assert.Equal(2, shared.SessionCount);
        Assert.Equal(FileActivityKind.Project, shared.ActivityKind);

        Assert.Contains(
            results,
            file => file.FilePath.Equals(
                @"C:\dev\proj-a\src\Relative.cs",
                StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            results,
            file => file.FilePath.Equals(
                @"C:\dev\proj-b\src\Relative.cs",
                StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            results,
            file => file.FilePath.Equals(
                @"C:\dev\proj-a\src\DriveRelative.cs",
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SearchFilesAsync_PathFragment_MatchesAcrossSlashDirectionAndCase()
    {
        var results = await _repository.SearchFilesAsync(
            @"SRC\program.cs",
            10,
            TestContext.Current.CancellationToken);

        var result = Assert.Single(results);
        Assert.Equal("src/Program.cs", result.FilePath);
        Assert.Equal(new DateTimeOffset(2025, 1, 1, 10, 2, 0, TimeSpan.Zero), result.FirstSeenAt);
        Assert.Equal(result.FirstSeenAt, result.LastSeenAt);
    }

    [Fact]
    public async Task SearchFilesAsync_EmptyQuery_ReturnsMostRecentlyRecordedPathsFirst()
    {
        await ExecuteAsync(
            """
            INSERT INTO session_files (session_id, file_path, tool_name, turn_index, first_seen_at)
            VALUES ('sess-2', 'src/Newest.cs', 'edit', 1, '2025-02-01T10:00:00Z');
            """);

        var results = await _repository.SearchFilesAsync(
            "",
            10,
            TestContext.Current.CancellationToken);

        Assert.Equal("src/Newest.cs", results[0].FilePath);
    }

    [Fact]
    public async Task SearchFilesAsync_UnicodeAndWhitespace_UsesSharedNormalization()
    {
        await ExecuteAsync(
            """
            INSERT INTO session_files (session_id, file_path, tool_name, turn_index, first_seen_at)
            VALUES ('sess-2', '  C:\Ärea\File.cs  ', 'edit', 1, NULL);
            """);

        var results = await _repository.SearchFilesAsync(
            @"c:/ärea/file.cs",
            10,
            TestContext.Current.CancellationToken);

        var result = Assert.Single(results);
        Assert.Equal(@"  C:\Ärea\File.cs  ", result.FilePath);
        Assert.Null(result.FirstSeenAt);
        Assert.Null(result.LastSeenAt);
    }

    [Fact]
    public async Task SearchFilesAsync_OrdinalIgnoreCase_HandlesFinalSigma()
    {
        await ExecuteAsync(
            """
            INSERT INTO session_files (session_id, file_path, tool_name, turn_index, first_seen_at)
            VALUES ('sess-2', 'C:\Σ\File.cs', 'edit', 1, '2025-02-01T10:00:00Z');
            """);

        var results = await _repository.SearchFilesAsync(
            @"c:/ς/file.cs",
            10,
            TestContext.Current.CancellationToken);

        Assert.Equal(@"C:\Σ\File.cs", Assert.Single(results).FilePath);
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
    public async Task GetFileHistoryAsync_CanonicalPathMatchesRelativeRecordedPath()
    {
        var results = await _repository.GetFileHistoryAsync(
            @"C:\dev\proj-a\src\Program.cs",
            TestContext.Current.CancellationToken);

        Assert.Equal("sess-1", Assert.Single(results).SessionId);
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
        await ExecuteAsync(
            """
            INSERT INTO checkpoints (session_id, checkpoint_number, title, overview, created_at)
            VALUES ('sess-1', 2, 'Latest checkpoint', 'Latest overview', '2025-01-02T11:00:00Z');
            """);

        var results = await _repository.GetFileHistoryAsync("src/Program.cs", TestContext.Current.CancellationToken);

        Assert.Equal("Latest overview", results[0].CheckpointOverview);
    }

    [Fact]
    public async Task GetFileHistoryAsync_NormalizedVariants_ReturnOneLatestEntryPerSession()
    {
        await ExecuteAsync(
            """
            INSERT INTO session_files (session_id, file_path, tool_name, turn_index, first_seen_at)
            VALUES ('sess-1', 'SRC\Program.cs', 'create', 2, '2025-02-01T10:00:00Z');
            """);

        var results = await _repository.GetFileHistoryAsync(
            @"src\PROGRAM.cs",
            TestContext.Current.CancellationToken);

        var result = Assert.Single(results);
        Assert.Equal("sess-1", result.SessionId);
        Assert.Equal(@"SRC\Program.cs", result.RecordedPath);
        Assert.Equal("create", result.ToolName);
    }

    [Fact]
    public async Task GetFileHistoryAsync_UnicodeAndWhitespace_UsesSharedNormalization()
    {
        await ExecuteAsync(
            """
            INSERT INTO session_files (session_id, file_path, tool_name, turn_index, first_seen_at)
            VALUES ('sess-2', '  C:\Ärea\File.cs  ', 'edit', 1, NULL);
            """);

        var results = await _repository.GetFileHistoryAsync(
            @"c:/ärea/file.cs",
            TestContext.Current.CancellationToken);

        var result = Assert.Single(results);
        Assert.Equal("sess-2", result.SessionId);
        Assert.Null(result.FirstSeenAt);
        Assert.Equal(@"  C:\Ärea\File.cs  ", result.RecordedPath);
    }

    [Fact]
    public async Task GetFileHistoryAsync_OrdinalIgnoreCase_HandlesFinalSigma()
    {
        await ExecuteAsync(
            """
            INSERT INTO session_files (session_id, file_path, tool_name, turn_index, first_seen_at)
            VALUES ('sess-2', 'C:\Σ\File.cs', 'edit', 1, '2025-02-01T10:00:00Z');
            """);

        var results = await _repository.GetFileHistoryAsync(
            @"c:/ς/file.cs",
            TestContext.Current.CancellationToken);

        Assert.Equal("sess-2", Assert.Single(results).SessionId);
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

    [Fact]
    public async Task GetResumeSuggestionsAsync_NegativeLimitReturnsAllSuggestions()
    {
        var results = await _repository.GetResumeSuggestionsAsync(-1, TestContext.Current.CancellationToken);

        Assert.Single(results);
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
