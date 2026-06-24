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
    public async Task SnapshotAsync_DetectedWindowWithTabs_UpsertsEachSessionIndividually()
    {
        var detector = new Mock<ILiveWindowDetector>();
        detector.Setup(d => d.DetectWindows()).Returns([Window(100, "s1", "s2")]);

        var repository = new Mock<ITerminalWindowsRepository>();
        repository.Setup(r => r.GetOpenAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var snapshotter = new TerminalWindowSnapshotter(detector.Object, repository.Object);

        await snapshotter.SnapshotAsync(Now, retentionCount: 50, Ct);

        // Each session in a terminal window is tracked as its own record (keyed by its own
        // single-session composition), so one OS terminal hosting many sessions does not collapse
        // them into a single record that can never individually close.
        var keyS1 = TerminalWindowComposition.Key(["s1"]);
        var keyS2 = TerminalWindowComposition.Key(["s2"]);
        repository.Verify(
            r => r.UpsertOpenAsync(
                100, keyS1,
                It.Is<IReadOnlyList<TerminalWindowTab>>(t => t.Count == 1 && t[0].SessionId == "s1"),
                Now, It.IsAny<CancellationToken>()),
            Times.Once);
        repository.Verify(
            r => r.UpsertOpenAsync(
                100, keyS2,
                It.Is<IReadOnlyList<TerminalWindowTab>>(t => t.Count == 1 && t[0].SessionId == "s2"),
                Now, It.IsAny<CancellationToken>()),
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
    public async Task SnapshotAsync_OneSessionInSharedTerminalVanishes_OnlyThatSessionIsClosed()
    {
        // The real-world failure: many session windows share ONE WindowsTerminal.exe process, so
        // the terminal pid never vanishes. s1 is still live; s2 has been closed. s2 must still be
        // detected as closed individually even though its terminal pid (100) is still alive via s1.
        var detector = new Mock<ILiveWindowDetector>();
        detector.Setup(d => d.DetectWindows()).Returns([Window(100, "s1")]);

        var repository = new Mock<ITerminalWindowsRepository>();
        repository.Setup(r => r.GetOpenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([OpenRecord("rec-s1", 100, "s1"), OpenRecord("rec-s2", 100, "s2")]);

        var snapshotter = new TerminalWindowSnapshotter(detector.Object, repository.Object);

        await snapshotter.SnapshotAsync(Now, retentionCount: 50, Ct);

        repository.Verify(r => r.CloseAsync("rec-s2", Now, It.IsAny<CancellationToken>()), Times.Once);
        repository.Verify(r => r.CloseAsync("rec-s1", It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
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
