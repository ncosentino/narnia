using System.IO.Abstractions.TestingHelpers;
using NexusLabs.Narnia.Core.Services;

namespace NexusLabs.Narnia.Core.Tests;

public sealed class GitArtifactInspectorTests
{
    [Fact]
    public async Task InspectAsync_LinkedWorktreeMarker_IsUnsafeWithoutRunningGit()
    {
        const string sessionDirectory = @"C:\copilot\session-state\session-1";
        var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [$@"{sessionDirectory}\files\repo\.git"] =
                new MockFileData("gitdir: C:\\repo\\.git\\worktrees\\session-1"),
        });
        var inspector = new GitArtifactInspector(fileSystem);

        var result = await inspector.InspectAsync(
            sessionDirectory,
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSafe);
        Assert.Contains(
            result.Reasons,
            reason => reason.Contains("linked Git worktree", StringComparison.Ordinal));
    }

    [Fact]
    public async Task InspectAsync_NoGitOrReparseMarkers_IsSafe()
    {
        const string sessionDirectory = @"C:\copilot\session-state\session-1";
        var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [$@"{sessionDirectory}\files\notes.md"] = new MockFileData("notes"),
        });
        var inspector = new GitArtifactInspector(fileSystem);

        var result = await inspector.InspectAsync(
            sessionDirectory,
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSafe);
        Assert.Empty(result.Reasons);
    }
}
