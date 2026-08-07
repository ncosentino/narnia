using System.IO.Abstractions.TestingHelpers;
using Moq;
using NexusLabs.Narnia.Core.Models;
using NexusLabs.Narnia.Core.Repositories;
using NexusLabs.Narnia.Core.Services;

namespace NexusLabs.Narnia.Core.Tests;

public sealed class LaunchCollisionDetectorTests
{
    private const string MainRepo = @"C:\dev\nexus-labs\veritas";
    private const string Worktree = @"C:\dev\nexus-labs\artifact0";
    private const string Launching = "11111111-1111-4111-8111-111111111111";
    private const string LiveSession = "22222222-2222-4222-8222-222222222222";
    private const string SecondLaunching = "33333333-3333-4333-8333-333333333333";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly Mock<ICopilotSessionActivityReader> _activity = new();
    private readonly Mock<ISessionRepository> _sessions = new();
    private readonly Mock<ISessionOverridesRepository> _overrides = new();
    private readonly Mock<IWorkspaceReader> _workspaces = new();
    private readonly MockFileSystem _fileSystem = new();

    public LaunchCollisionDetectorTests()
    {
        _fileSystem.AddDirectory(MainRepo);
        _fileSystem.AddDirectory(Worktree);
        _activity.Setup(reader => reader.GetActiveSessionIds())
            .Returns(new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        _sessions.Setup(repo => repo.GetByIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Session?)null);
        _overrides.Setup(repo => repo.GetOverrideAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SessionOverride?)null);
        _workspaces.Setup(reader => reader.ReadWorkspace(It.IsAny<string>()))
            .Returns((string id) => new WorkspaceInfo(id, null, []));
    }

    private LaunchCollisionDetector Build() =>
        new(_activity.Object, _sessions.Object, _overrides.Object, _workspaces.Object, _fileSystem);

    private void GivenLiveSession(string sessionId, string? cwd, string? localPath = null, string? name = null)
    {
        _activity.Setup(reader => reader.GetActiveSessionIds())
            .Returns(new HashSet<string>([sessionId], StringComparer.OrdinalIgnoreCase));
        _sessions.Setup(repo => repo.GetByIdAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Session(sessionId, cwd, name));
        if (localPath is not null)
        {
            _overrides.Setup(repo => repo.GetOverrideAsync(sessionId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Override(sessionId, localPath, name));
        }
    }

    private static Session Session(string id, string? cwd, string? name = null) =>
        new(id, cwd, null, null, name, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private static SessionOverride Override(string id, string? localPath, string? name = null) =>
        new(id, name, null, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)
        {
            LocalPath = localPath,
        };

    /// <summary>
    /// The production failure this guard exists for: two sessions whose override paths both point at
    /// the main repository, so relaunching one lands a second agent in a tree that is already in use.
    /// </summary>
    [Fact]
    public async Task DetectAsync_LiveSessionInSameDirectory_IsReported()
    {
        GivenLiveSession(LiveSession, MainRepo, name: "Veritas");

        var collisions = await Build().DetectAsync(
            [new TerminalLaunchTab(Launching, "Veritas ArtA", MainRepo)],
            Ct);

        var collision = Assert.Single(collisions);
        Assert.Equal(Launching, collision.SessionId);
        Assert.Equal(LiveSession, collision.OccupyingSessionId);
        Assert.Equal("Veritas", collision.OccupyingSessionName);
        Assert.True(collision.OccupyingIsLive);
    }

    [Fact]
    public async Task DetectAsync_LiveSessionInDifferentWorktree_IsNotACollision()
    {
        GivenLiveSession(LiveSession, MainRepo);

        var collisions = await Build().DetectAsync(
            [new TerminalLaunchTab(Launching, "Veritas ArtA", Worktree)],
            Ct);

        Assert.Empty(collisions);
    }

    // Git spells worktree paths with forward slashes, and users type trailing separators. Neither
    // spelling may be allowed to hide a genuine collision.
    [Fact]
    public async Task DetectAsync_DirectorySpelledDifferently_StillCollides()
    {
        GivenLiveSession(LiveSession, "C:/dev/nexus-labs/veritas");

        var collisions = await Build().DetectAsync(
            [new TerminalLaunchTab(Launching, "Veritas ArtA", MainRepo + @"\")],
            Ct);

        Assert.Single(collisions);
    }

    // Reopening a session that is already running is the documented recovery path; the existing
    // resume-safety and operation-coordinator guards own that case.
    [Fact]
    public async Task DetectAsync_RelaunchingTheLiveSessionItself_IsNotACollision()
    {
        GivenLiveSession(Launching, MainRepo);

        var collisions = await Build().DetectAsync(
            [new TerminalLaunchTab(Launching, "Veritas", MainRepo)],
            Ct);

        Assert.Empty(collisions);
    }

    [Fact]
    public async Task DetectAsync_TwoTabsInOneBatchSharingADirectory_IsReported()
    {
        var collisions = await Build().DetectAsync(
            [
                new TerminalLaunchTab(Launching, "Veritas", MainRepo),
                new TerminalLaunchTab(SecondLaunching, "Veritas ArtA", MainRepo),
            ],
            Ct);

        var collision = Assert.Single(collisions);
        Assert.Equal(SecondLaunching, collision.SessionId);
        Assert.Equal(Launching, collision.OccupyingSessionId);
        Assert.False(collision.OccupyingIsLive);
    }

    // The override wins over the session store when launching, so occupancy must follow the same
    // precedence or the guard would check a directory the session will not actually use.
    [Fact]
    public async Task DetectAsync_LiveSessionOverridePathBeatsSessionStoreCwd()
    {
        GivenLiveSession(LiveSession, cwd: Worktree, localPath: MainRepo);

        var collisions = await Build().DetectAsync(
            [new TerminalLaunchTab(Launching, "Veritas ArtA", MainRepo)],
            Ct);

        Assert.Single(collisions);
    }

    [Fact]
    public async Task DetectAsync_LiveSessionDirectoryMissingFromDisk_IsIgnored()
    {
        GivenLiveSession(LiveSession, @"C:\dev\deleted-repo");

        var collisions = await Build().DetectAsync(
            [new TerminalLaunchTab(Launching, "Veritas", MainRepo)],
            Ct);

        Assert.Empty(collisions);
    }

    [Fact]
    public async Task DetectAsync_TabWithNoDirectory_IsIgnored()
    {
        GivenLiveSession(LiveSession, MainRepo);

        var collisions = await Build().DetectAsync(
            [new TerminalLaunchTab(Launching, "Veritas", null)],
            Ct);

        Assert.Empty(collisions);
    }

    [Fact]
    public async Task DetectAsync_FallsBackToWorkspaceGitRootWhenNothingElseResolves()
    {
        _activity.Setup(reader => reader.GetActiveSessionIds())
            .Returns(new HashSet<string>([LiveSession], StringComparer.OrdinalIgnoreCase));
        _sessions.Setup(repo => repo.GetByIdAsync(LiveSession, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Session(LiveSession, cwd: null));
        _workspaces.Setup(reader => reader.ReadWorkspace(LiveSession))
            .Returns(new WorkspaceInfo(LiveSession, MainRepo, []));

        var collisions = await Build().DetectAsync(
            [new TerminalLaunchTab(Launching, "Veritas", MainRepo)],
            Ct);

        Assert.Single(collisions);
    }

    // Occupancy is advisory. Enumerating processes throws InvalidOperationException when a process
    // exits between enumeration and reading its id — a routine race on a busy machine. Letting it
    // escape would 500 the launch endpoint, and the UI only offers a force retry on 409, so the
    // user would be unable to launch at all.
    [Theory]
    [MemberData(nameof(NonFatalActivityFailures))]
    public async Task DetectAsync_ActivityReaderThrows_DoesNotBlockLaunch(Exception failure)
    {
        _activity.Setup(reader => reader.GetActiveSessionIds()).Throws(failure);

        var collisions = await Build().DetectAsync(
            [new TerminalLaunchTab(Launching, "Veritas", MainRepo)],
            Ct);

        Assert.Empty(collisions);
    }

    public static TheoryData<Exception> NonFatalActivityFailures() =>
    [
        new InvalidOperationException("process exited during enumeration"),
        new System.ComponentModel.Win32Exception("access denied"),
        new UnauthorizedAccessException("no process access"),
        new IOException("lock directory unreadable"),
    ];

    // A repository failure must degrade the same way; it is still only occupancy information.
    [Fact]
    public async Task DetectAsync_SessionRepositoryThrows_DoesNotBlockLaunch()
    {
        _activity.Setup(reader => reader.GetActiveSessionIds())
            .Returns(new HashSet<string>([LiveSession], StringComparer.OrdinalIgnoreCase));
        _sessions.Setup(repo => repo.GetByIdAsync(LiveSession, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database is locked"));

        var collisions = await Build().DetectAsync(
            [new TerminalLaunchTab(Launching, "Veritas", MainRepo)],
            Ct);

        Assert.Empty(collisions);
    }

    // Cancellation is not a degradable failure — it must still propagate to the caller.
    [Fact]
    public async Task DetectAsync_Cancelled_StillThrows()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        _activity.Setup(reader => reader.GetActiveSessionIds())
            .Returns(new HashSet<string>([LiveSession], StringComparer.OrdinalIgnoreCase));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Build().DetectAsync(
                [new TerminalLaunchTab(Launching, "Veritas", MainRepo)],
                cancelled.Token).AsTask());
    }

    [Fact]
    public void Describe_LiveAndPendingOccupants_ReadDifferently()
    {
        var live = new LaunchDirectoryCollision(Launching, MainRepo, LiveSession, "Veritas", true);
        var pending = new LaunchDirectoryCollision(Launching, MainRepo, LiveSession, "Veritas", false);

        Assert.Contains("already running", live.Describe(), StringComparison.Ordinal);
        Assert.Contains("also being launched", pending.Describe(), StringComparison.Ordinal);
    }

    [Fact]
    public void Describe_UnnamedOccupant_FallsBackToShortSessionId()
    {
        var collision = new LaunchDirectoryCollision(Launching, MainRepo, LiveSession, null, true);

        Assert.Contains("22222222", collision.Describe(), StringComparison.Ordinal);
    }
}
