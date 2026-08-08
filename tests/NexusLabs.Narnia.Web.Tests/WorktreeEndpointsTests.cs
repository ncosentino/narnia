using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using NexusLabs.Narnia.Core.Models;
using NexusLabs.Narnia.Core.Services;

namespace NexusLabs.Narnia.Web.Tests;

public sealed class WorktreeEndpointsTests
{
    private const string MainRepo = @"C:\dev\example\app";
    private const string ArtifactWorktree = @"C:\dev\example\app-feature";
    private const string MainBranch = "feature/main-work";
    private const string ArtifactBranch = "feature/side-work";
    private const string SessionA = "11111111-1111-4111-8111-111111111111";
    private const string SessionB = "22222222-2222-4222-8222-222222222222";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static SessionWorktreeAdvice Advice(
        IReadOnlyList<GitWorktree> worktrees,
        IReadOnlyList<WorktreeAdvisory> advisories) =>
        new(SessionA, MainRepo, MainBranch, "legacy-label", worktrees, advisories);

    [Fact]
    public async Task GetWorktrees_ReturnsSelectableWorktreesAndMarksTheCurrentOne()
    {
        using var factory = new NarniaWebAppFactory();
        factory.WorktreeAdvisor
            .Setup(advisor => advisor.AdviseAsync(SessionA, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Advice(
                [
                    new GitWorktree(MainRepo, MainBranch, "aaa", false, false, true, true),
                    new GitWorktree(ArtifactWorktree, ArtifactBranch, "bbb", false, false, false, true),
                ],
                []));
        var client = factory.CreateClient();

        var document = await client.GetFromJsonAsync<JsonElement>(
            $"/api/sessions/{SessionA}/worktrees",
            Ct);

        var worktrees = document.GetProperty("worktrees").EnumerateArray().ToArray();
        Assert.Equal(2, worktrees.Length);
        Assert.True(worktrees[0].GetProperty("isCurrent").GetBoolean());
        Assert.False(worktrees[1].GetProperty("isCurrent").GetBoolean());
        Assert.Equal(ArtifactBranch, worktrees[1].GetProperty("branch").GetString());
        Assert.Equal(MainRepo, document.GetProperty("resolvedDirectory").GetString());
    }

    [Fact]
    public async Task GetWorktrees_SurfacesAdvisoriesWithTheirSuggestedWorktree()
    {
        using var factory = new NarniaWebAppFactory();
        factory.WorktreeAdvisor
            .Setup(advisor => advisor.AdviseAsync(SessionA, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Advice(
                [],
                [
                    new WorktreeAdvisory(
                        WorktreeAdvisoryKind.BranchInDifferentWorktree,
                        "Branch lives elsewhere.",
                        ArtifactWorktree,
                        ArtifactBranch),
                ]));
        var client = factory.CreateClient();

        var document = await client.GetFromJsonAsync<JsonElement>(
            $"/api/sessions/{SessionA}/worktrees",
            Ct);

        var advisory = Assert.Single(document.GetProperty("advisories").EnumerateArray().ToArray());
        Assert.Equal("BranchInDifferentWorktree", advisory.GetProperty("kind").GetString());
        Assert.Equal(ArtifactWorktree, advisory.GetProperty("suggestedPath").GetString());
        Assert.Equal(ArtifactBranch, advisory.GetProperty("suggestedBranch").GetString());
    }

    [Fact]
    public async Task GetWorktrees_RejectsAnIdThatIsNotASessionGuid()
    {
        using var factory = new NarniaWebAppFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/sessions/not-a-guid/worktrees", Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// A launch into an occupied directory must be refused with 409 so the UI can confirm, rather
    /// than silently placing a second agent in a working tree another agent already owns.
    /// </summary>
    [Fact]
    public async Task Launch_DirectoryAlreadyOccupied_IsRefusedWithConflict()
    {
        using var factory = new NarniaWebAppFactory();
        GivenSession(factory, SessionA, MainRepo);
        GivenCollision(factory);
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/launch",
            new { sessionId = SessionA, target = "cwd" },
            Ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("directory-collision", body.GetProperty("error").GetString());
        var collision = Assert.Single(body.GetProperty("collisions").EnumerateArray().ToArray());
        Assert.Equal(SessionB, collision.GetProperty("occupyingSessionId").GetString());
        Assert.True(collision.GetProperty("occupyingIsLive").GetBoolean());
        factory.ProcessLauncher.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Launch_WithForce_ProceedsWithoutConsultingTheDetector()
    {
        using var factory = new NarniaWebAppFactory();
        GivenSession(factory, SessionA, MainRepo);
        GivenCollision(factory);
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/launch",
            new { sessionId = SessionA, target = "cwd", force = true },
            Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        factory.LaunchCollisionDetector.Verify(
            detector => detector.DetectAsync(
                It.IsAny<IReadOnlyList<TerminalLaunchTab>>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Launch_NoCollision_Proceeds()
    {
        using var factory = new NarniaWebAppFactory();
        GivenSession(factory, SessionA, MainRepo);
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/launch",
            new { sessionId = SessionA, target = "cwd" },
            Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task LaunchBulk_DirectoryCollision_IsRefusedWithConflictAndLaunchesNothing()
    {
        using var factory = new NarniaWebAppFactory();
        GivenSession(factory, SessionA, MainRepo);
        GivenSession(factory, SessionB, MainRepo);
        GivenCollision(factory);
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/launch-bulk",
            new { sessionIds = new[] { SessionA, SessionB } },
            Ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("directory-collision", body.GetProperty("error").GetString());
        factory.ProcessLauncher.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task LaunchBulk_WithForce_SkipsTheCollisionCheck()
    {
        using var factory = new NarniaWebAppFactory();
        GivenSession(factory, SessionA, MainRepo);
        GivenCollision(factory);
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/launch-bulk",
            new { sessionIds = new[] { SessionA }, force = true },
            Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        factory.LaunchCollisionDetector.Verify(
            detector => detector.DetectAsync(
                It.IsAny<IReadOnlyList<TerminalLaunchTab>>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static void GivenSession(NarniaWebAppFactory factory, string sessionId, string cwd)
    {
        // The launch endpoints require the directory to exist on disk before they build a tab, so a
        // real path is used here; nothing is written to it.
        var existing = Directory.Exists(cwd) ? cwd : Path.GetTempPath();
        factory.SessionRepository
            .Setup(repo => repo.GetByIdAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Session(
                sessionId, existing, null, null, "Example", null,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
    }

    private static void GivenCollision(NarniaWebAppFactory factory) =>
        factory.LaunchCollisionDetector
            .Setup(detector => detector.DetectAsync(
                It.IsAny<IReadOnlyList<TerminalLaunchTab>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<LaunchDirectoryCollision>)
            [
                new LaunchDirectoryCollision(SessionA, MainRepo, SessionB, "Example", true),
            ]);
}
