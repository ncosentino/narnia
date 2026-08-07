using NexusLabs.Narnia.Core.Services;

namespace NexusLabs.Narnia.Core.Tests;

public sealed class GitWorktreePorcelainTests
{
    /// <summary>
    /// Real output captured from <c>git worktree list --porcelain</c> in a multi-worktree
    /// repository. Pinned verbatim so a format regression fails here rather than silently
    /// producing an empty picker.
    /// </summary>
    private const string RealOutput =
        "worktree C:/dev/nexus-labs/veritas\n" +
        "HEAD 05e88b8f8037809d14d8d5298d8c43f250714aa5\n" +
        "branch refs/heads/feature/filesystem-evidence-architecture-496\n" +
        "\n" +
        "worktree C:/dev/nexus-labs/artifact0\n" +
        "HEAD b1a069c1a1b2c3d4e5f60718293a4b5c6d7e8f90\n" +
        "branch refs/heads/feature/png-graded-excavation\n" +
        "\n";

    private static bool AllExist(string path) => true;

    [Fact]
    public void Parse_RealOutput_ReturnsEveryWorktreeWithShortBranchNames()
    {
        var worktrees = GitWorktreePorcelain.Parse(RealOutput, AllExist);

        Assert.Equal(2, worktrees.Count);
        Assert.Equal("feature/filesystem-evidence-architecture-496", worktrees[0].Branch);
        Assert.Equal("feature/png-graded-excavation", worktrees[1].Branch);
        Assert.Equal("05e88b8f8037809d14d8d5298d8c43f250714aa5", worktrees[0].Head);
    }

    // Git reports forward slashes even on Windows; Narnia stores backslashes. Comparisons against
    // an override path only work if the parser canonicalizes on the way in.
    [Fact]
    public void Parse_NormalizesGitForwardSlashesToHostSeparators()
    {
        var worktrees = GitWorktreePorcelain.Parse(RealOutput, AllExist);

        Assert.True(DirectoryPaths.AreSame(@"C:\dev\nexus-labs\veritas", worktrees[0].Path));
        Assert.True(DirectoryPaths.AreSame(@"C:\dev\nexus-labs\artifact0\", worktrees[1].Path));
    }

    [Fact]
    public void Parse_MarksOnlyTheFirstEntryAsPrimary()
    {
        var worktrees = GitWorktreePorcelain.Parse(RealOutput, AllExist);

        Assert.True(worktrees[0].IsPrimary);
        Assert.False(worktrees[1].IsPrimary);
    }

    // The final record has no trailing blank line when Git's output is truncated or piped; without
    // an explicit flush it would be dropped entirely.
    [Fact]
    public void Parse_LastRecordWithoutTrailingBlankLine_IsStillReturned()
    {
        const string output =
            "worktree C:/repo\nHEAD aaa\nbranch refs/heads/main\n" +
            "\n" +
            "worktree C:/repo-two\nHEAD bbb\nbranch refs/heads/topic";

        var worktrees = GitWorktreePorcelain.Parse(output, AllExist);

        Assert.Equal(2, worktrees.Count);
        Assert.Equal("topic", worktrees[1].Branch);
    }

    [Fact]
    public void Parse_DetachedWorktree_HasNoBranchAndIsFlagged()
    {
        const string output = "worktree C:/repo\nHEAD abc123\ndetached\n\n";

        var worktree = Assert.Single(GitWorktreePorcelain.Parse(output, AllExist));

        Assert.Null(worktree.Branch);
        Assert.True(worktree.IsDetached);
    }

    [Fact]
    public void Parse_BareRepository_IsFlaggedAndKeepsNoBranch()
    {
        const string output = "worktree C:/repo.git\nbare\n\nworktree C:/repo\nHEAD abc\nbranch refs/heads/main\n\n";

        var worktrees = GitWorktreePorcelain.Parse(output, AllExist);

        Assert.True(worktrees[0].IsBare);
        Assert.Null(worktrees[0].Branch);
        Assert.False(worktrees[1].IsBare);
    }

    [Fact]
    public void Parse_CarriageReturnLineEndings_AreHandled()
    {
        const string output = "worktree C:/repo\r\nHEAD abc\r\nbranch refs/heads/main\r\n\r\n";

        var worktree = Assert.Single(GitWorktreePorcelain.Parse(output, AllExist));

        Assert.Equal("main", worktree.Branch);
        Assert.Equal("abc", worktree.Head);
    }

    // A branch name that is not under refs/heads (Git can report a full symbolic ref) must not be
    // truncated into something that no longer names the branch.
    [Fact]
    public void Parse_BranchOutsideRefsHeads_IsKeptVerbatim()
    {
        const string output = "worktree C:/repo\nHEAD abc\nbranch refs/remotes/origin/main\n\n";

        var worktree = Assert.Single(GitWorktreePorcelain.Parse(output, AllExist));

        Assert.Equal("refs/remotes/origin/main", worktree.Branch);
    }

    [Fact]
    public void Parse_PrunedWorktree_IsReportedAsMissing()
    {
        const string output = "worktree C:/gone\nHEAD abc\nbranch refs/heads/main\n\n";

        var worktree = Assert.Single(GitWorktreePorcelain.Parse(output, _ => false));

        Assert.False(worktree.Exists);
    }

    [Fact]
    public void Parse_EmptyOutput_ReturnsNothing()
    {
        Assert.Empty(GitWorktreePorcelain.Parse("", AllExist));
        Assert.Empty(GitWorktreePorcelain.Parse("   \n\n", AllExist));
    }
}
