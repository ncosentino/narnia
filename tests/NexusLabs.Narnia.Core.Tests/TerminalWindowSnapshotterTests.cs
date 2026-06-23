using NexusLabs.Narnia.Core.Models;
using NexusLabs.Narnia.Core.Repositories;
using NexusLabs.Narnia.Core.Services;

namespace NexusLabs.Narnia.Core.Tests;

public sealed class TerminalWindowSnapshotterTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 22, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static DetectedWindow Window(int terminalPid, params string[] sessionIds) =>
        new(terminalPid, sessionIds.Select((s, i) => new DetectedTab(s, i, null, null)).ToList());

    private static TerminalWindow OpenRecord(string id, int terminalPid, params string[] sessionIds) =>
        new(
            id, Name: null, Pinned: false, Source: "live", Status: TerminalWindowStatus.Open,
            TerminalProcessId: terminalPid, CompositionKey: TerminalWindowComposition.Key(sessionIds),
            OccurrenceCount: 1, FirstSeenAt: Now, LastSeenAt: Now, ClosedAt: null,
            Tabs: sessionIds.Select((s, i) => new TerminalWindowTab(s, i, null)).ToList());

    [Fact]
    public async Task SnapshotAsync_NewDetectedWindow_IsUpserted()
    {
        var detector = new Mock<ILiveWindowDetector>();
        detector.Setup(d => d.DetectWindows()).Returns([Window(100, "s1", "s2")]);

        var repository = new Mock<ITerminalWindowsRepository>();
        repository.Setup(r => r.GetOpenAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var snapshotter = new TerminalWindowSnapshotter(detector.Object, repository.Object);

        await snapshotter.SnapshotAsync(Now, retentionCount: 50, Ct);

        var expectedKey = TerminalWindowComposition.Key(["s1", "s2"]);
        repository.Verify(
            r => r.UpsertOpenAsync(
                100,
                expectedKey,
                It.Is<IReadOnlyList<TerminalWindowTab>>(t => t.Count == 2),
                Now,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SnapshotAsync_StillOpenWindow_IsUpsertedAndNotClosed()
    {
        var detector = new Mock<ILiveWindowDetector>();
        detector.Setup(d => d.DetectWindows()).Returns([Window(100, "s1")]);

        var repository = new Mock<ITerminalWindowsRepository>();
        repository.Setup(r => r.GetOpenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([OpenRecord("w-100", 100, "s1")]);

        var snapshotter = new TerminalWindowSnapshotter(detector.Object, repository.Object);

        await snapshotter.SnapshotAsync(Now, retentionCount: 50, Ct);

        repository.Verify(
            r => r.UpsertOpenAsync(100, It.IsAny<string>(), It.IsAny<IReadOnlyList<TerminalWindowTab>>(), Now, It.IsAny<CancellationToken>()),
            Times.Once);
        repository.Verify(r => r.CloseAsync(It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SnapshotAsync_VanishedWindow_IsClosed()
    {
        var detector = new Mock<ILiveWindowDetector>();
        detector.Setup(d => d.DetectWindows()).Returns([Window(100, "s1")]);

        var repository = new Mock<ITerminalWindowsRepository>();
        repository.Setup(r => r.GetOpenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([OpenRecord("w-100", 100, "s1"), OpenRecord("w-200", 200, "s2")]);

        var snapshotter = new TerminalWindowSnapshotter(detector.Object, repository.Object);

        await snapshotter.SnapshotAsync(Now, retentionCount: 50, Ct);

        repository.Verify(r => r.CloseAsync("w-200", Now, It.IsAny<CancellationToken>()), Times.Once);
        repository.Verify(r => r.CloseAsync("w-100", It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SnapshotAsync_AlwaysPrunesWithRetentionBound()
    {
        var detector = new Mock<ILiveWindowDetector>();
        detector.Setup(d => d.DetectWindows()).Returns([]);

        var repository = new Mock<ITerminalWindowsRepository>();
        repository.Setup(r => r.GetOpenAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var snapshotter = new TerminalWindowSnapshotter(detector.Object, repository.Object);

        await snapshotter.SnapshotAsync(Now, retentionCount: 25, Ct);

        repository.Verify(r => r.PruneClosedAsync(25, It.IsAny<CancellationToken>()), Times.Once);
    }
}
