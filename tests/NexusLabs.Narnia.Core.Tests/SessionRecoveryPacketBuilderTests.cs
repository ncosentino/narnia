using System.IO.Abstractions.TestingHelpers;
using NexusLabs.Narnia.Core.Configuration;
using NexusLabs.Narnia.Core.Models;
using NexusLabs.Narnia.Core.Repositories;
using NexusLabs.Narnia.Core.Services;

namespace NexusLabs.Narnia.Core.Tests;

public sealed class SessionRecoveryPacketBuilderTests
{
    private const string SourceId = "33333333-3333-4333-8333-333333333333";
    private const string ReplacementId = "44444444-4444-4444-8444-444444444444";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task BuildAsync_PreservesHistoryTasksAndBootstrapInstructions()
    {
        var now = new DateTimeOffset(2026, 7, 24, 12, 0, 0, TimeSpan.Zero);
        var sessions = new Mock<ISessionRepository>();
        sessions
            .Setup(repository => repository.GetByIdAsync(
                SourceId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Session(
                SourceId,
                @"C:\repo",
                "owner/repo",
                "feature",
                "Recovered work",
                @"C:\repo",
                now.AddDays(-2),
                now,
                2,
                1));
        sessions
            .Setup(repository => repository.GetCheckpointsAsync(
                SourceId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new Checkpoint(
                    1,
                    SourceId,
                    1,
                    "Checkpoint",
                    "Overview",
                    "History",
                    "Work done",
                    "Details",
                    "file.cs",
                    "Next steps",
                    now),
            ]);
        sessions
            .Setup(repository => repository.GetTurnsAsync(
                SourceId,
                0,
                2,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new Turn(
                    1,
                    SourceId,
                    0,
                    "Initial request",
                    new string('x', 500_000),
                    now),
                new Turn(2, SourceId, 1, "Follow-up", "Follow-up response", now),
            ]);
        var overrides = new Mock<ISessionOverridesRepository>();
        overrides
            .Setup(repository => repository.GetOverrideAsync(
                SourceId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SessionOverride(
                SourceId,
                "Alias",
                "owner/repo",
                "feature",
                "Important notes " + new string('n', 100_000),
                now,
                now)
            {
                IsFavorite = true,
                LocalPath = @"C:\repo",
                TerminalTitle = "Recovered work",
            });
        var workspace = new Mock<IWorkspaceReader>();
        workspace
            .Setup(reader => reader.ReadWorkspace(SourceId))
            .Returns(new WorkspaceInfo(SourceId, @"C:\repo", ["plan.md"])
            {
                ParentTaskId = "task-1",
                ParentSessionId = "parent-1",
            });
        var tasks = new Mock<ISessionTaskStateReader>();
        tasks
            .Setup(reader => reader.Read(SourceId))
            .Returns(new SessionTaskState(
                [new SessionTaskItem("todo", "Finish", "Complete work", "in_progress", now, now)],
                [],
                null));
        var fileSystem = new MockFileSystem();
        var builder = new SessionRecoveryPacketBuilder(
            sessions.Object,
            overrides.Object,
            workspace.Object,
            tasks.Object,
            new NarniaOptions { RecoveryDirectory = "C:\\narnia\\recoveries\\" },
            fileSystem);

        var result = await builder.BuildAsync(SourceId, ReplacementId, Ct);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.PacketPath);
        Assert.True(fileSystem.File.Exists(result.PacketPath));
        var packet = fileSystem.File.ReadAllText(result.PacketPath);
        Assert.Contains("Initial request", packet, StringComparison.Ordinal);
        Assert.Contains("Checkpoint", packet, StringComparison.Ordinal);
        Assert.Contains("[in_progress] Finish", packet, StringComparison.Ordinal);
        Assert.Contains("Important notes", packet, StringComparison.Ordinal);
        Assert.Contains("Follow-up response", packet, StringComparison.Ordinal);
        Assert.Contains("Content truncated", packet, StringComparison.Ordinal);
        Assert.Contains("plan.md", packet, StringComparison.Ordinal);
        Assert.True(result.PacketTruncated);
        Assert.NotNull(result.BootstrapPrompt);
        Assert.Contains("get_session_recovery_packet", result.BootstrapPrompt, StringComparison.Ordinal);
        Assert.Contains("Follow-up response", result.BootstrapPrompt, StringComparison.Ordinal);
        Assert.True(result.BootstrapPrompt.Length <= 70_000);
    }
}
