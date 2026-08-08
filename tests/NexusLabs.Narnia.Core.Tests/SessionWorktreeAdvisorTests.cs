using System.IO.Abstractions.TestingHelpers;
using Moq;
using NexusLabs.Narnia.Core.Models;
using NexusLabs.Narnia.Core.Repositories;
using NexusLabs.Narnia.Core.Services;

namespace NexusLabs.Narnia.Core.Tests;

public sealed class SessionWorktreeAdvisorTests
{
    private const string MainRepo = @"C:\dev\nexus-labs\veritas";
    private const string ArtifactWorktree = @"C:\dev\nexus-labs\artifact0";
    private const string MainBranch = "feature/filesystem-evidence-architecture-496";
    private const string ArtifactBranch = "feature/png-graded-excavation";
    private const string SessionId = "11111111-1111-4111-8111-111111111111";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly Mock<ISessionRepository> _sessions = new();
    private readonly Mock<ISessionOverridesRepository> _overrides = new();
    private readonly Mock<IWorkspaceReader> _workspaces = new();
    private readonly Mock<IGitWorktreeReader> _worktrees = new();
    private readonly MockFileSystem _fileSystem = new();

    public SessionWorktreeAdvisorTests()
    {
        _fileSystem.AddDirectory(MainRepo);
        _fileSystem.AddDirectory(ArtifactWorktree);
        _sessions.Setup(repo => repo.GetByIdAsync(SessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Session(
                SessionId, MainRepo, null, "main", "Veritas", null,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        _overrides.Setup(repo => repo.GetOverrideAsync(SessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SessionOverride?)null);
        _workspaces.Setup(reader => reader.ReadWorkspace(It.IsAny<string>()))
            .Returns((string id) => new WorkspaceInfo(id, null, []));
        _worktrees.Setup(reader => reader.FindBranchAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GitBranchPresence.Missing);
        GivenWorktrees(
            new GitWorktree(MainRepo, MainBranch, "aaa", false, false, true, true),
            new GitWorktree(ArtifactWorktree, ArtifactBranch, "bbb", false, false, false, true));
    }

    private void GivenWorktrees(params GitWorktree[] worktrees) =>
        _worktrees.Setup(reader => reader.ReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GitWorktreeInspection(true, worktrees, null));

    // Branches are assumed missing unless a test says otherwise, so a test that expects silence has
    // to state the reason explicitly rather than getting it by default.
    private void GivenBranchPresence(string branch, GitBranchPresence presence) =>
        _worktrees.Setup(reader => reader.FindBranchAsync(
                It.IsAny<string>(), branch, It.IsAny<CancellationToken>()))
            .ReturnsAsync(presence);

    private void GivenBranchOverride(string? branch, string? localPath = null) =>
        _overrides.Setup(repo => repo.GetOverrideAsync(SessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SessionOverride(
                SessionId, null, null, branch, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)
            {
                LocalPath = localPath,
            });

    private SessionWorktreeAdvisor Build() =>
        new(_sessions.Object, _overrides.Object, _workspaces.Object, _worktrees.Object, _fileSystem);

    [Fact]
    public async Task AdviseAsync_NoBranchOverride_ProducesNoAdvisories()
    {
        var advice = await Build().AdviseAsync(SessionId, Ct);

        Assert.Empty(advice.Advisories);
        Assert.Equal(2, advice.Worktrees.Count);
        Assert.Equal(MainBranch, advice.ResolvedBranch);
    }

    /// <summary>
    /// The exact production defect: a session labelled with a branch name that exists nowhere, while
    /// its launch directory is the main repository on a completely different branch.
    /// </summary>
    [Fact]
    public async Task AdviseAsync_BranchOverrideNamesNoRealBranch_IsReported()
    {
        GivenBranchOverride("worktree-art-a", localPath: MainRepo);

        var advice = await Build().AdviseAsync(SessionId, Ct);

        var advisory = Assert.Single(advice.Advisories);
        Assert.Equal(WorktreeAdvisoryKind.BranchNotFound, advisory.Kind);
        Assert.Contains("worktree-art-a", advisory.Message, StringComparison.Ordinal);
        Assert.Contains(MainBranch, advisory.Message, StringComparison.Ordinal);
        Assert.Null(advisory.SuggestedPath);
    }

    /// <summary>
    /// Switching branches in place is ordinary Git use, so a label naming a real branch that simply
    /// is not checked out anywhere right now must stay silent. This was the dominant case in
    /// practice — 19 of 23 recorded branch overrides on the author's machine were <c>main</c>, in a
    /// repository normally sitting on a feature branch — and warning about it buried the one real
    /// misconfiguration.
    /// </summary>
    [Fact]
    public async Task AdviseAsync_RealBranchThatIsSimplyNotCheckedOut_IsSilent()
    {
        GivenBranchOverride("main", localPath: MainRepo);
        GivenBranchPresence("main", GitBranchPresence.Exists);

        var advice = await Build().AdviseAsync(SessionId, Ct);

        Assert.Empty(advice.Advisories);
    }

    // A warning needs positive evidence that the branch is missing. If the existence check could not
    // answer — Git absent, or timed out — saying nothing is the honest outcome.
    [Fact]
    public async Task AdviseAsync_BranchPresenceUnknown_StaysSilentRatherThanGuessing()
    {
        GivenBranchOverride("main", localPath: MainRepo);
        GivenBranchPresence("main", GitBranchPresence.Unknown);

        var advice = await Build().AdviseAsync(SessionId, Ct);

        Assert.Empty(advice.Advisories);
    }

    // Existence is only consulted when the branch is not checked out anywhere; a branch found in a
    // worktree is proof enough, and the extra Git call would be wasted on every page load.
    [Fact]
    public async Task AdviseAsync_BranchCheckedOutSomewhere_DoesNotRunTheExistenceCheck()
    {
        GivenBranchOverride(ArtifactBranch, localPath: MainRepo);

        await Build().AdviseAsync(SessionId, Ct);

        _worktrees.Verify(
            reader => reader.FindBranchAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // The actionable case: the branch is real, just checked out somewhere the session never launches.
    [Fact]
    public async Task AdviseAsync_BranchOverrideLivesInAnotherWorktree_SuggestsThatWorktree()
    {
        GivenBranchOverride(ArtifactBranch, localPath: MainRepo);

        var advice = await Build().AdviseAsync(SessionId, Ct);

        var advisory = Assert.Single(advice.Advisories);
        Assert.Equal(WorktreeAdvisoryKind.BranchInDifferentWorktree, advisory.Kind);
        Assert.Equal(ArtifactWorktree, advisory.SuggestedPath);
        Assert.Equal(ArtifactBranch, advisory.SuggestedBranch);
    }

    [Fact]
    public async Task AdviseAsync_CoherentOverridePair_ProducesNoAdvisories()
    {
        GivenBranchOverride(ArtifactBranch, localPath: ArtifactWorktree);

        var advice = await Build().AdviseAsync(SessionId, Ct);

        Assert.Empty(advice.Advisories);
        Assert.Equal(ArtifactWorktree, advice.ResolvedDirectory);
        Assert.Equal(ArtifactBranch, advice.ResolvedBranch);
    }

    // The override path is what actually decides the launch directory, so advice must be computed
    // against it rather than against the session store's recorded working directory.
    [Fact]
    public async Task AdviseAsync_OverridePathBeatsSessionStoreCwd()
    {
        GivenBranchOverride(null, localPath: ArtifactWorktree);

        var advice = await Build().AdviseAsync(SessionId, Ct);

        Assert.Equal(ArtifactWorktree, advice.ResolvedDirectory);
        Assert.Equal(ArtifactBranch, advice.ResolvedBranch);
    }

    [Fact]
    public async Task AdviseAsync_OverridePathMissingFromDisk_FallsBackToSessionCwd()
    {
        GivenBranchOverride(null, localPath: @"C:\dev\deleted");

        var advice = await Build().AdviseAsync(SessionId, Ct);

        Assert.Equal(MainRepo, advice.ResolvedDirectory);
    }

    /// <summary>
    /// Working outside version control is an ordinary state — every Narnia scheduled job runs from
    /// the Windows system directory, for instance. A session that has claimed no branch has nothing
    /// incoherent to report, so it must produce no warning at all.
    /// </summary>
    [Theory]
    [InlineData(GitWorktreeFailure.NotARepository, "fatal: not a git repository")]
    [InlineData(GitWorktreeFailure.GitNotAvailable, "Git could not be started: not found")]
    [InlineData(GitWorktreeFailure.TimedOut, "Git timed out listing worktrees.")]
    [InlineData(GitWorktreeFailure.DirectoryUnavailable, "Directory not found: C:\\gone")]
    public async Task AdviseAsync_NotARepositoryAndNoBranchClaimed_IsSilent(
        GitWorktreeFailure failure,
        string error)
    {
        GivenInspectionFailure(failure, error);

        var advice = await Build().AdviseAsync(SessionId, Ct);

        Assert.Empty(advice.Advisories);
        Assert.Empty(advice.Worktrees);
    }

    [Fact]
    public async Task AdviseAsync_NoDirectoryAndNoBranchClaimed_IsSilent()
    {
        _sessions.Setup(repo => repo.GetByIdAsync(SessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Session?)null);

        var advice = await Build().AdviseAsync(SessionId, Ct);

        Assert.Null(advice.ResolvedDirectory);
        Assert.Empty(advice.Advisories);
    }

    // A branch label on a session that is not in a repository is genuinely incoherent, so it is
    // still reported — but as an unverifiable label, not as a worktree mismatch.
    [Fact]
    public async Task AdviseAsync_DirectoryIsNotARepositoryButABranchIsClaimed_ReportsIt()
    {
        GivenBranchOverride("worktree-art-a", localPath: MainRepo);
        GivenInspectionFailure(
            GitWorktreeFailure.NotARepository,
            "fatal: not a git repository (or any of the parent directories): .git");

        var advice = await Build().AdviseAsync(SessionId, Ct);

        var advisory = Assert.Single(advice.Advisories);
        Assert.Equal(WorktreeAdvisoryKind.NotARepository, advisory.Kind);
        Assert.Contains("worktree-art-a", advisory.Message, StringComparison.Ordinal);
        Assert.Empty(advice.Worktrees);
    }

    // A missing Git executable is an environment problem worth naming; conflating it with "not a
    // repository" would send the user looking in the wrong place.
    [Fact]
    public async Task AdviseAsync_GitExecutableMissing_IsDistinguishedFromNotARepository()
    {
        GivenBranchOverride(ArtifactBranch, localPath: MainRepo);
        GivenInspectionFailure(
            GitWorktreeFailure.GitNotAvailable,
            "Git could not be started: The system cannot find the file specified.");

        var advice = await Build().AdviseAsync(SessionId, Ct);

        Assert.Equal(WorktreeAdvisoryKind.GitUnavailable, Assert.Single(advice.Advisories).Kind);
    }

    /// <summary>
    /// Only a Git exit code proves a directory is not a repository. A timeout or a vanished
    /// directory means the check never ran, and claiming "not a repository" would tell the user to
    /// stop looking for exactly the misconfiguration this advisor exists to surface.
    /// </summary>
    [Theory]
    [InlineData(GitWorktreeFailure.TimedOut, "Git timed out listing worktrees.")]
    [InlineData(GitWorktreeFailure.DirectoryUnavailable, "Directory not found: C:\\gone")]
    public async Task AdviseAsync_CheckDidNotComplete_IsNeverReportedAsNotARepository(
        GitWorktreeFailure failure,
        string error)
    {
        GivenBranchOverride(ArtifactBranch, localPath: MainRepo);
        GivenInspectionFailure(failure, error);

        var advice = await Build().AdviseAsync(SessionId, Ct);

        var advisory = Assert.Single(advice.Advisories);
        Assert.Equal(WorktreeAdvisoryKind.GitUnavailable, advisory.Kind);
        Assert.DoesNotContain("is not inside a Git repository", advisory.Message, StringComparison.Ordinal);
        Assert.Contains("could not be checked", advisory.Message, StringComparison.Ordinal);
    }

    private void GivenInspectionFailure(GitWorktreeFailure failure, string error) =>
        _worktrees.Setup(reader => reader.ReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GitWorktreeInspection(false, [], error, failure));

    // Workspace metadata is only a last-resort fallback; a malformed workspace file must not fail
    // the whole advisory request.
    [Fact]
    public async Task AdviseAsync_WorkspaceReaderThrows_StillReturnsAdvice()
    {
        _sessions.Setup(repo => repo.GetByIdAsync(SessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Session(
                SessionId, null, null, null, "Veritas", null,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        _overrides.Setup(repo => repo.GetOverrideAsync(SessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SessionOverride(
                SessionId, null, null, ArtifactBranch, null,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        _workspaces.Setup(reader => reader.ReadWorkspace(It.IsAny<string>()))
            .Throws(new InvalidOperationException("malformed workspace.yaml"));

        var advice = await Build().AdviseAsync(SessionId, Ct);

        Assert.Null(advice.ResolvedDirectory);
        Assert.Equal(WorktreeAdvisoryKind.NotARepository, Assert.Single(advice.Advisories).Kind);
    }

    [Fact]
    public async Task AdviseAsync_NoResolvableDirectory_ReportsWithoutThrowing()
    {
        GivenBranchOverride(ArtifactBranch);
        _sessions.Setup(repo => repo.GetByIdAsync(SessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Session?)null);

        var advice = await Build().AdviseAsync(SessionId, Ct);

        Assert.Null(advice.ResolvedDirectory);
        Assert.Empty(advice.Worktrees);
        Assert.Equal(WorktreeAdvisoryKind.NotARepository, Assert.Single(advice.Advisories).Kind);
    }

    // Git reports forward slashes; the override stores backslashes. If these did not compare equal
    // a perfectly coherent configuration would be flagged as a mismatch on every page load.
    [Fact]
    public async Task AdviseAsync_GitPathSpellingDiffersFromOverride_StillMatchesCurrentWorktree()
    {
        GivenWorktrees(
            new GitWorktree("C:/dev/nexus-labs/veritas", MainBranch, "aaa", false, false, true, true));
        GivenBranchOverride(MainBranch, localPath: MainRepo);

        var advice = await Build().AdviseAsync(SessionId, Ct);

        Assert.Empty(advice.Advisories);
        Assert.Equal(MainBranch, advice.ResolvedBranch);
    }

    [Fact]
    public async Task AdviseAsync_BlankBranchOverride_IsTreatedAsAbsent()
    {
        GivenBranchOverride("   ", localPath: MainRepo);

        var advice = await Build().AdviseAsync(SessionId, Ct);

        Assert.Null(advice.BranchOverride);
        Assert.Empty(advice.Advisories);
    }
}
