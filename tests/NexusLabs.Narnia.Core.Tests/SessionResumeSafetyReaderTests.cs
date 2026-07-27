using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Text;
using NexusLabs.Narnia.Core.Configuration;
using NexusLabs.Narnia.Core.Models;
using NexusLabs.Narnia.Core.Repositories;

namespace NexusLabs.Narnia.Core.Tests;

public sealed class SessionResumeSafetyReaderTests
{
    private const string SessionId = "11111111-1111-4111-8111-111111111111";
    private const string Root = @"C:\copilot\session-state";
    private static string SessionDirectory => $@"{Root}\{SessionId}";

    [Fact]
    public void Inspect_NestedHistoryWithoutSessionStart_IsIncompatible()
    {
        var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [$@"{SessionDirectory}\events.jsonl"] = new(
                """{"type":"system.message","id":"event-1","data":{}}""" + "\n"),
            [$@"{SessionDirectory}\workspace.yaml"] = new(
                "mc_task_id: task-1\nmc_session_id: parent-1\n"),
        });
        var workspaceReader = new WorkspaceReader(Options(), fileSystem);
        var reader = new SessionResumeSafetyReader(Options(), fileSystem, workspaceReader);

        var result = reader.Inspect(SessionId);

        Assert.Equal(SessionResumeSafety.Incompatible, result.Safety);
        Assert.Equal("system.message", result.FirstEventType);
        Assert.True(result.IsNestedAgent);
        Assert.Contains("nested Copilot agent", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Inspect_HistoryStartingWithSessionStart_IsResumable()
    {
        var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [$@"{SessionDirectory}\events.jsonl"] = new(
                """{"type":"session.start","id":"event-1","data":{}}""" + "\n"),
        });
        var workspaceReader = new WorkspaceReader(Options(), fileSystem);
        var reader = new SessionResumeSafetyReader(Options(), fileSystem, workspaceReader);

        var result = reader.Inspect(SessionId);

        Assert.Equal(SessionResumeSafety.Resumable, result.Safety);
        Assert.Null(result.Reason);
    }

    [Fact]
    public void Inspect_HistoryAboveCopilotStringLimit_IsIncompatible()
    {
        const long characterLimit = 128;
        var content =
            """{"type":"session.start","id":"event-1","data":{}}""" + "\n" +
            new string('a', 200);
        var reader = CreateReader(
            content,
            Encoding.UTF8.GetByteCount(content),
            characterLimit);

        var result = reader.Inspect(SessionId);

        Assert.Equal(SessionResumeSafety.Incompatible, result.Safety);
        Assert.Equal("session.start", result.FirstEventType);
        Assert.Contains("128-character", result.Reason, StringComparison.Ordinal);
        Assert.Contains("blank session", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Inspect_MultibyteFileAboveRawByteLimitButBelowCharacterLimit_IsResumable()
    {
        const long characterLimit = 128;
        var content =
            """{"type":"session.start","id":"event-1","data":{}}""" + "\n" +
            new string('界', 40);
        var bytes = Encoding.UTF8.GetByteCount(content);
        Assert.True(bytes > characterLimit);
        Assert.True(content.Length < characterLimit);
        var reader = CreateReader(content, bytes, characterLimit);

        var result = reader.Inspect(SessionId);

        Assert.Equal(SessionResumeSafety.Resumable, result.Safety);
        Assert.Null(result.Reason);
    }

    [Fact]
    public void Inspect_MissingEventStream_IsUnknown()
    {
        var fileSystem = new MockFileSystem();
        var workspaceReader = new WorkspaceReader(Options(), fileSystem);
        var reader = new SessionResumeSafetyReader(Options(), fileSystem, workspaceReader);

        var result = reader.Inspect(SessionId);

        Assert.Equal(SessionResumeSafety.Unknown, result.Safety);
    }

    [Fact]
    public void Inspect_InvalidFirstEvent_IsIncompatible()
    {
        var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [$@"{SessionDirectory}\events.jsonl"] = new("{invalid\n"),
        });
        var workspaceReader = new WorkspaceReader(Options(), fileSystem);
        var reader = new SessionResumeSafetyReader(Options(), fileSystem, workspaceReader);

        var result = reader.Inspect(SessionId);

        Assert.Equal(SessionResumeSafety.Incompatible, result.Safety);
        Assert.Contains("invalid JSON", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Inspect_TraversalIdentifier_DoesNotReadWorkspace()
    {
        var fileSystem = new MockFileSystem();
        var workspace = new Mock<IWorkspaceReader>();
        var reader = new SessionResumeSafetyReader(Options(), fileSystem, workspace.Object);

        var result = reader.Inspect(@"..\outside");

        Assert.Equal(SessionResumeSafety.Incompatible, result.Safety);
        workspace.Verify(candidate => candidate.ReadMetadata(It.IsAny<string>()), Times.Never);
    }

    [Theory]
    [InlineData("""{"type":123}""")]
    [InlineData("""["session.start"]""")]
    public void Inspect_InvalidEventShape_IsIncompatible(string firstEvent)
    {
        var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [$@"{SessionDirectory}\events.jsonl"] = new(firstEvent + "\n"),
        });
        var workspaceReader = new WorkspaceReader(Options(), fileSystem);
        var reader = new SessionResumeSafetyReader(Options(), fileSystem, workspaceReader);

        var result = reader.Inspect(SessionId);

        Assert.Equal(SessionResumeSafety.Incompatible, result.Safety);
        Assert.Contains("valid string event type", result.Reason, StringComparison.Ordinal);
    }

    private static NarniaOptions Options() => new()
    {
        SessionStatePath = Root,
    };

    private static SessionResumeSafetyReader CreateReader(
        string content,
        long reportedLength,
        long characterLimit)
    {
        var baseFileSystem = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [$@"{SessionDirectory}\events.jsonl"] = new(content),
        });
        var lastWriteTimeUtc = new DateTime(2026, 7, 26, 12, 0, 0, DateTimeKind.Utc);
        var fileInfo = new Mock<IFileInfo>();
        fileInfo.SetupGet(candidate => candidate.Length).Returns(reportedLength);
        fileInfo.SetupGet(candidate => candidate.LastWriteTimeUtc).Returns(lastWriteTimeUtc);
        var fileInfoFactory = new Mock<IFileInfoFactory>();
        fileInfoFactory
            .Setup(candidate => candidate.New($@"{SessionDirectory}\events.jsonl"))
            .Returns(fileInfo.Object);
        var fileSystem = new Mock<IFileSystem>();
        fileSystem.SetupGet(candidate => candidate.Path).Returns(baseFileSystem.Path);
        fileSystem.SetupGet(candidate => candidate.File).Returns(baseFileSystem.File);
        fileSystem.SetupGet(candidate => candidate.FileInfo).Returns(fileInfoFactory.Object);
        var workspace = new Mock<IWorkspaceReader>();
        workspace.Setup(candidate => candidate.ReadMetadata(SessionId))
            .Returns(new WorkspaceInfo(SessionId, null, []));
        return new SessionResumeSafetyReader(
            Options(),
            fileSystem.Object,
            workspace.Object,
            characterLimit);
    }
}
