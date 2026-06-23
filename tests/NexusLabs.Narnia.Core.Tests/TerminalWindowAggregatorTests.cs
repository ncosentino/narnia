using NexusLabs.Narnia.Core.Models;
using NexusLabs.Narnia.Core.Services;

namespace NexusLabs.Narnia.Core.Tests;

public sealed class TerminalWindowAggregatorTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static TerminalWindow Window(string id, TerminalWindowStatus status, DateTimeOffset lastSeen) =>
        new(
            id, Name: null, Pinned: false, Source: "live", Status: status,
            TerminalProcessId: status == TerminalWindowStatus.Open ? 1 : null,
            CompositionKey: id, OccurrenceCount: 1,
            FirstSeenAt: lastSeen, LastSeenAt: lastSeen,
            ClosedAt: status == TerminalWindowStatus.Closed ? lastSeen : null,
            Tabs: []);

    private static Mock<ITerminalWindowSource> Source(TerminalWindowSnapshot snapshot)
    {
        var mock = new Mock<ITerminalWindowSource>();
        mock.SetupGet(s => s.SourceId).Returns("test");
        mock.Setup(s => s.GetWindowsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);
        return mock;
    }

    [Fact]
    public async Task GetWindowsAsync_MergesSourcesAndOrdersByRecency()
    {
        var baseTime = new DateTimeOffset(2026, 6, 22, 12, 0, 0, TimeSpan.Zero);
        var sourceA = Source(new TerminalWindowSnapshot(
            [Window("open-old", TerminalWindowStatus.Open, baseTime)],
            [Window("closed-old", TerminalWindowStatus.Closed, baseTime)]));
        var sourceB = Source(new TerminalWindowSnapshot(
            [Window("open-new", TerminalWindowStatus.Open, baseTime.AddMinutes(5))],
            [Window("closed-new", TerminalWindowStatus.Closed, baseTime.AddMinutes(5))]));

        var aggregator = new TerminalWindowAggregator([sourceA.Object, sourceB.Object]);

        var result = await aggregator.GetWindowsAsync(50, Ct);

        Assert.Equal(["open-new", "open-old"], result.Open.Select(w => w.Id));
        Assert.Equal(["closed-new", "closed-old"], result.Closed.Select(w => w.Id));
    }

    [Fact]
    public async Task GetWindowsAsync_CapsClosedAtRequestedLimit()
    {
        var baseTime = new DateTimeOffset(2026, 6, 22, 12, 0, 0, TimeSpan.Zero);
        var source = Source(new TerminalWindowSnapshot(
            [],
            [
                Window("c1", TerminalWindowStatus.Closed, baseTime.AddMinutes(1)),
                Window("c2", TerminalWindowStatus.Closed, baseTime.AddMinutes(2)),
                Window("c3", TerminalWindowStatus.Closed, baseTime.AddMinutes(3)),
            ]));

        var aggregator = new TerminalWindowAggregator([source.Object]);

        var result = await aggregator.GetWindowsAsync(2, Ct);

        Assert.Equal(["c3", "c2"], result.Closed.Select(w => w.Id));
    }

    [Fact]
    public async Task GetWindowsAsync_NoSources_ReturnsEmpty()
    {
        var aggregator = new TerminalWindowAggregator([]);

        var result = await aggregator.GetWindowsAsync(50, Ct);

        Assert.Empty(result.Open);
        Assert.Empty(result.Closed);
    }
}
