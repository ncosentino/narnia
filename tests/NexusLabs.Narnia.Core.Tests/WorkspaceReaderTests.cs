using System.IO.Abstractions.TestingHelpers;
using NexusLabs.Narnia.Core.Configuration;
using NexusLabs.Narnia.Core.Repositories;

namespace NexusLabs.Narnia.Core.Tests;

public sealed class WorkspaceReaderTests
{
    private const string SessionId = "test-session-id";
    private const string SessionStatePath = @"C:\copilot\session-state";
    private static string SessionDir => $@"{SessionStatePath}\{SessionId}";

    private static NarniaOptions CreateOptions() => new()
    {
        SessionStatePath = SessionStatePath,
    };

    [Fact]
    public void ReadWorkspace_NoSessionDir_ReturnsEmptyInfo()
    {
        var fs = new MockFileSystem();
        var reader = new WorkspaceReader(CreateOptions(), fs);

        var result = reader.ReadWorkspace(SessionId);

        Assert.Equal(SessionId, result.SessionId);
        Assert.Null(result.GitRoot);
        Assert.Empty(result.ArtifactFiles);
    }

    [Fact]
    public void ReadWorkspace_WorkspaceYamlWithGitRoot_ReturnsGitRoot()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [$@"{SessionDir}\workspace.yaml"] = new MockFileData("git_root: C:\\dev\\my-project\n"),
        });
        var reader = new WorkspaceReader(CreateOptions(), fs);

        var result = reader.ReadWorkspace(SessionId);

        Assert.Equal(@"C:\dev\my-project", result.GitRoot);
    }

    [Fact]
    public void ReadWorkspace_WorkspaceYamlNoGitRoot_ReturnsNullGitRoot()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [$@"{SessionDir}\workspace.yaml"] = new MockFileData("some_key: some_value\n"),
        });
        var reader = new WorkspaceReader(CreateOptions(), fs);

        var result = reader.ReadWorkspace(SessionId);

        Assert.Null(result.GitRoot);
    }

    [Fact]
    public void ReadWorkspace_WorkspaceYamlMultipleKeys_ParsesGitRootCorrectly()
    {
        var content = "cwd: C:\\dev\\project\ngit_root: C:\\dev\\project\nbranch: main\n";
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [$@"{SessionDir}\workspace.yaml"] = new MockFileData(content),
        });
        var reader = new WorkspaceReader(CreateOptions(), fs);

        var result = reader.ReadWorkspace(SessionId);

        Assert.Equal(@"C:\dev\project", result.GitRoot);
    }

    [Fact]
    public void ReadWorkspace_FilesDirectory_ReturnsArtifactFileNames()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [$@"{SessionDir}\files\plan.md"] = new MockFileData("# Plan"),
            [$@"{SessionDir}\files\context.md"] = new MockFileData("# Context"),
        });
        var reader = new WorkspaceReader(CreateOptions(), fs);

        var result = reader.ReadWorkspace(SessionId);

        Assert.Equal(2, result.ArtifactFiles.Length);
        Assert.Contains("plan.md", result.ArtifactFiles);
        Assert.Contains("context.md", result.ArtifactFiles);
    }

    [Fact]
    public void ReadWorkspace_NoFilesDirectory_ReturnsEmptyArtifacts()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [$@"{SessionDir}\workspace.yaml"] = new MockFileData("git_root: C:\\dev\\x\n"),
        });
        var reader = new WorkspaceReader(CreateOptions(), fs);

        var result = reader.ReadWorkspace(SessionId);

        Assert.Empty(result.ArtifactFiles);
    }

    [Fact]
    public void ReadWorkspace_WorkspaceYamlAndFiles_ReturnsBothGitRootAndArtifacts()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [$@"{SessionDir}\workspace.yaml"] = new MockFileData("git_root: C:\\repos\\narnia\n"),
            [$@"{SessionDir}\files\plan.md"] = new MockFileData("plan"),
        });
        var reader = new WorkspaceReader(CreateOptions(), fs);

        var result = reader.ReadWorkspace(SessionId);

        Assert.Equal(@"C:\repos\narnia", result.GitRoot);
        Assert.Single(result.ArtifactFiles);
        Assert.Equal("plan.md", result.ArtifactFiles[0]);
    }
}
