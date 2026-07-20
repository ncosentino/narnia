using System.IO.Abstractions.TestingHelpers;
using NexusLabs.Narnia.Core.Configuration;
using NexusLabs.Narnia.Core.Models;
using NexusLabs.Narnia.Core.Repositories;
using NexusLabs.Narnia.Core.Services;

namespace NexusLabs.Narnia.Core.Tests;

public sealed class SessionStorageScannerTests
{
    private const string Root = @"C:\copilot\session-state";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ScanAsync_ClassifiesLogicalBytesAndGitMarkers()
    {
        const string sessionId = "11111111-1111-4111-8111-111111111111";
        var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [$@"{Root}\{sessionId}\events.jsonl"] = Data(10),
            [$@"{Root}\{sessionId}\session.db"] = Data(11),
            [$@"{Root}\{sessionId}\checkpoints\001.md"] = Data(12),
            [$@"{Root}\{sessionId}\rewind-snapshots\001.json"] = Data(13),
            [$@"{Root}\{sessionId}\files\repo\source.cs"] = Data(14),
            [$@"{Root}\{sessionId}\research\report.md"] = Data(15),
            [$@"{Root}\{sessionId}\plan.md"] = Data(16),
        });
        fileSystem.AddDirectory($@"{Root}\{sessionId}\files\repo\.git");
        var repository = new Mock<ISessionStorageRepository>();
        var workspaceReader = new Mock<IWorkspaceReader>();
        workspaceReader
            .Setup(reader => reader.ReadMetadata(sessionId))
            .Returns(new WorkspaceInfo(sessionId, null, []) { IsUserNamed = true });
        IReadOnlyList<SessionStorageRecord>? saved = null;
        repository
            .Setup(repo => repo.SaveScanAsync(
                It.IsAny<IReadOnlyList<SessionStorageRecord>>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<CancellationToken>()))
            .Callback((IReadOnlyList<SessionStorageRecord> records, DateTimeOffset _, DateTimeOffset _, CancellationToken _) =>
                saved = records)
            .Returns(ValueTask.CompletedTask);
        var now = new DateTimeOffset(2026, 7, 19, 12, 0, 0, TimeSpan.Zero);
        var scanner = new SessionStorageScanner(
            new NarniaOptions { SessionStatePath = Root },
            fileSystem,
            workspaceReader.Object,
            repository.Object,
            new FixedTimeProvider(now));

        var result = await scanner.ScanAsync(
            new Progress<(int Scanned, int Total)>(),
            Ct);

        var record = Assert.Single(result);
        Assert.Same(result, saved);
        Assert.Equal(91, record.TotalBytes);
        Assert.Equal(10, record.EventsBytes);
        Assert.Equal(11, record.SessionDatabaseBytes);
        Assert.Equal(12, record.CheckpointsBytes);
        Assert.Equal(13, record.RewindBytes);
        Assert.Equal(29, record.ArtifactsBytes);
        Assert.Equal(16, record.OtherBytes);
        Assert.Equal(7, record.FileCount);
        Assert.Equal("plan.md", record.LargestFilePath);
        Assert.True(record.ContainsGitRepository);
        Assert.True(record.IsUserNamed);
        Assert.False(record.ContainsLinkedWorktree);
        Assert.True(record.IsComplete);
    }

    [Fact]
    public async Task ScanAsync_MissingRootRecordsFailure()
    {
        var repository = new Mock<ISessionStorageRepository>();
        var workspaceReader = new Mock<IWorkspaceReader>();
        repository
            .Setup(repo => repo.RecordScanFailureAsync(
                It.IsAny<DateTimeOffset>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);
        var scanner = new SessionStorageScanner(
            new NarniaOptions { SessionStatePath = Root },
            new MockFileSystem(),
            workspaceReader.Object,
            repository.Object,
            new FixedTimeProvider(DateTimeOffset.UtcNow));

        await Assert.ThrowsAsync<DirectoryNotFoundException>(
            async () => await scanner.ScanAsync(
                new Progress<(int Scanned, int Total)>(),
                Ct));
        repository.Verify(repo => repo.RecordScanFailureAsync(
            It.IsAny<DateTimeOffset>(),
            It.IsAny<DateTimeOffset>(),
            It.Is<string>(error => error.Contains(Root, StringComparison.Ordinal)),
            It.IsAny<CancellationToken>()));
        repository.Verify(repo => repo.SaveScanAsync(
            It.IsAny<IReadOnlyList<SessionStorageRecord>>(),
            It.IsAny<DateTimeOffset>(),
            It.IsAny<DateTimeOffset>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    private static MockFileData Data(int length) => new(new string('x', length));

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
