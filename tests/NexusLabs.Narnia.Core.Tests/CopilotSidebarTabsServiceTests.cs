using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;
using NexusLabs.Narnia.Core.Configuration;
using NexusLabs.Narnia.Core.Models;
using NexusLabs.Narnia.Core.Repositories;
using NexusLabs.Narnia.Core.Services;

namespace NexusLabs.Narnia.Core.Tests;

public sealed class CopilotSidebarTabsServiceTests
{
    private const string SidebarRoot = @"C:\copilot\sidebar-sessions-state";
    private const string SessionRoot = @"C:\copilot\session-state";
    private const string Cwd = @"C:\dev\nexus-labs\genesis";
    private const string SessionA = "11111111-1111-4111-8111-111111111111";
    private const string SessionB = "22222222-2222-4222-8222-222222222222";
    private const string SessionC = "33333333-3333-4333-8333-333333333333";

    private static readonly DateTimeOffset Now =
        new(2026, 8, 7, 4, 30, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// Pins the exact derivation Copilot uses. This value was captured from a real Copilot
    /// install: <c>C:\dev\nexus-labs\genesis</c> is stored as
    /// <c>f048a1ab...f0963.json</c>.
    /// </summary>
    [Fact]
    public void FileNameFor_MatchesCopilotObservedHash()
    {
        Assert.Equal(
            "f048a1abf9a914a8f5458b4b2a2792128a3a106998a6653ae0fb8ada6b7f0963.json",
            CopilotSidebarStatePath.FileNameFor(Cwd));
    }

    // Copilot hashes the raw bytes, so differing spellings of one folder are distinct tab lists.
    [Theory]
    [InlineData(@"c:\dev\nexus-labs\genesis")]
    [InlineData(@"C:/dev/nexus-labs/genesis")]
    [InlineData(@"C:\dev\nexus-labs\genesis\")]
    public void FileNameFor_DoesNotNormalizeThePath(string variant)
    {
        Assert.NotEqual(
            CopilotSidebarStatePath.FileNameFor(Cwd),
            CopilotSidebarStatePath.FileNameFor(variant));
    }

    [Fact]
    public async Task ListAsync_EnrichesTabsWithSessionMetadataAndOrder()
    {
        var service = BuildService(
            out _,
            files: new Dictionary<string, MockFileData>
            {
                [PathFor(Cwd)] = State(Cwd, SessionA, SessionB),
                [$@"{SessionRoot}\{SessionA}\events.jsonl"] = new string('x', 1024),
            },
            knownSessions: [SessionA],
            activeSessions: []);

        var workspaces = await service.ListAsync(Ct);

        var workspace = Assert.Single(workspaces);
        Assert.Equal(Cwd, workspace.Cwd);
        Assert.Equal(1, workspace.SchemaVersion);
        Assert.Equal(2, workspace.TabCount);
        Assert.Equal(1, workspace.UnknownTabCount);

        Assert.Equal(SessionA, workspace.Tabs[0].SessionId);
        Assert.Equal(0, workspace.Tabs[0].Position);
        Assert.True(workspace.Tabs[0].IsKnown);
        Assert.Equal(1024, workspace.Tabs[0].EventStreamBytes);

        Assert.Equal(SessionB, workspace.Tabs[1].SessionId);
        Assert.False(workspace.Tabs[1].IsKnown);
        Assert.Null(workspace.Tabs[1].EventStreamBytes);
    }

    [Fact]
    public async Task ListAsync_SurfacesUnparsableTabListInsteadOfDroppingIt()
    {
        var service = BuildService(
            out _,
            files: new Dictionary<string, MockFileData>
            {
                [$@"{SidebarRoot}\{new string('a', 64)}.json"] = "{ this is not json",
            },
            knownSessions: [],
            activeSessions: []);

        var workspace = Assert.Single(await service.ListAsync(Ct));

        Assert.NotNull(workspace.ParseError);
        Assert.Empty(workspace.Tabs);
    }

    [Fact]
    public async Task RemoveTabsAsync_DropsOnlyTheSelectedSessionAndBacksUpFirst()
    {
        var service = BuildService(
            out var fileSystem,
            files: new Dictionary<string, MockFileData>
            {
                [PathFor(Cwd)] = State(Cwd, SessionA, SessionB, SessionC),
            },
            knownSessions: [SessionA, SessionB, SessionC],
            activeSessions: []);

        var result = await service.RemoveTabsAsync(Cwd, [SessionB], force: false, Ct);

        Assert.True(result.Succeeded);
        Assert.Equal([SessionB], result.RemovedSessionIds);
        Assert.Equal(2, result.RemainingTabCount);
        Assert.NotNull(result.BackupPath);
        Assert.True(fileSystem.File.Exists(result.BackupPath));

        var rewritten = ReadState(fileSystem, Cwd);
        Assert.Equal([SessionA, SessionC], rewritten.SessionIds!);
        Assert.Equal(1, rewritten.SchemaVersion);
        Assert.Equal(Cwd, rewritten.Cwd);
    }

    /// <summary>
    /// Copilot parses this file with Node's <c>JSON.parse</c>, which rejects a leading byte-order
    /// mark. Reading the file back through a text API silently strips a BOM, so the bytes have to
    /// be asserted directly or a corrupt rewrite looks healthy.
    /// </summary>
    [Fact]
    public async Task RepairAsync_WritesBomFreeUtf8SoCopilotCanStillParseTheFile()
    {
        var service = BuildService(
            out var fileSystem,
            files: new Dictionary<string, MockFileData>
            {
                [PathFor(Cwd)] = State(Cwd, SessionA, SessionB),
            },
            knownSessions: [SessionA, SessionB],
            activeSessions: []);

        await service.RemoveTabsAsync(Cwd, [SessionB], force: false, Ct);

        var bytes = fileSystem.File.ReadAllBytes(PathFor(Cwd));
        Assert.Equal((byte)'{', bytes[0]);
        Assert.False(
            bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF,
            "The rewritten tab list must not start with a UTF-8 BOM.");
        Assert.DoesNotContain((byte)'\r', bytes);
    }

    [Fact]
    public async Task ResetAsync_ClearsEveryTabButKeepsTheFileReadableByCopilot()
    {
        var service = BuildService(
            out var fileSystem,
            files: new Dictionary<string, MockFileData>
            {
                [PathFor(Cwd)] = State(Cwd, SessionA, SessionB),
            },
            knownSessions: [SessionA, SessionB],
            activeSessions: []);

        var result = await service.ResetAsync(Cwd, force: false, Ct);

        Assert.True(result.Succeeded);
        Assert.Equal(0, result.RemainingTabCount);
        Assert.Equal([SessionA, SessionB], result.RemovedSessionIds);

        var rewritten = ReadState(fileSystem, Cwd);
        Assert.Empty(rewritten.SessionIds!);
        Assert.Equal(Cwd, rewritten.Cwd);
    }

    /// <summary>
    /// Copilot merges its in-memory tab list back over this file during shutdown, so a repair
    /// applied while a session is live is silently reverted. Refusing is the honest outcome.
    /// </summary>
    [Fact]
    public async Task ResetAsync_RefusesWhileACopilotRuntimeOwnsATabInTheWorkspace()
    {
        var service = BuildService(
            out var fileSystem,
            files: new Dictionary<string, MockFileData>
            {
                [PathFor(Cwd)] = State(Cwd, SessionA, SessionB),
            },
            knownSessions: [SessionA, SessionB],
            activeSessions: [SessionB]);

        var result = await service.ResetAsync(Cwd, force: false, Ct);

        Assert.False(result.Succeeded);
        Assert.Contains("still running", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal([SessionA, SessionB], ReadState(fileSystem, Cwd).SessionIds!);
    }

    [Fact]
    public async Task ResetAsync_AppliesUnderALiveRuntimeWhenForced()
    {
        var service = BuildService(
            out var fileSystem,
            files: new Dictionary<string, MockFileData>
            {
                [PathFor(Cwd)] = State(Cwd, SessionA),
            },
            knownSessions: [SessionA],
            activeSessions: [SessionA]);

        var result = await service.ResetAsync(Cwd, force: true, Ct);

        Assert.True(result.Succeeded);
        Assert.Empty(ReadState(fileSystem, Cwd).SessionIds!);
    }

    [Fact]
    public async Task ResetAsync_ReportsWorkspacesThatHaveNoTabList()
    {
        var service = BuildService(
            out _,
            files: new Dictionary<string, MockFileData>(),
            knownSessions: [],
            activeSessions: []);

        var result = await service.ResetAsync(Cwd, force: false, Ct);

        Assert.False(result.Succeeded);
        Assert.Contains("No sidebar tab list", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetAsync_FindsTheWorkspaceByHashedWorkingDirectory()
    {
        var service = BuildService(
            out _,
            files: new Dictionary<string, MockFileData>
            {
                [PathFor(Cwd)] = State(Cwd, SessionA),
            },
            knownSessions: [SessionA],
            activeSessions: [SessionA]);

        var workspace = await service.GetAsync(Cwd, Ct);

        Assert.NotNull(workspace);
        Assert.True(workspace!.HasLiveRuntime);
        Assert.Equal(1, workspace.LiveTabCount);
    }

    private static string PathFor(string cwd) =>
        $@"{SidebarRoot}\{CopilotSidebarStatePath.FileNameFor(cwd)}";

    private static string State(string cwd, params string[] sessionIds) =>
        JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            cwd,
            sessionIds,
        });

    private static StateShape ReadState(MockFileSystem fileSystem, string cwd) =>
        JsonSerializer.Deserialize<StateShape>(
            fileSystem.File.ReadAllText(PathFor(cwd)),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

    private static CopilotSidebarTabsService BuildService(
        out MockFileSystem fileSystem,
        IDictionary<string, MockFileData> files,
        IReadOnlyCollection<string> knownSessions,
        IReadOnlyCollection<string> activeSessions)
    {
        fileSystem = new MockFileSystem(files);
        fileSystem.AddDirectory(SidebarRoot);

        var sessions = new Mock<ISessionRepository>();
        sessions
            .Setup(repository => repository.GetByIdsAsync(
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<string> ids, CancellationToken _) =>
                (IReadOnlyDictionary<string, Session>)ids
                    .Where(knownSessions.Contains)
                    .ToDictionary(
                        id => id,
                        id => new Session(id, Cwd, "ncosentino/narnia", "main", $"Session {id[..4]}",
                            null, Now, Now),
                        StringComparer.OrdinalIgnoreCase));

        var activity = new Mock<ICopilotSessionActivityReader>();
        activity
            .Setup(reader => reader.GetActiveSessionIds())
            .Returns(new HashSet<string>(activeSessions, StringComparer.OrdinalIgnoreCase));

        return new CopilotSidebarTabsService(
            new NarniaOptions
            {
                SidebarStatePath = SidebarRoot,
                SessionStatePath = SessionRoot,
            },
            fileSystem,
            sessions.Object,
            activity.Object,
            new FakeTimeProvider(Now));
    }

    private sealed class StateShape
    {
        public int? SchemaVersion { get; set; }
        public string? Cwd { get; set; }
        public string[]? SessionIds { get; set; }
    }

    private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
