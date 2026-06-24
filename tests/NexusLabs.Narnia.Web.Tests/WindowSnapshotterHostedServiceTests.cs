using Microsoft.Extensions.Logging.Abstractions;
using NexusLabs.Narnia.Core.Configuration;
using NexusLabs.Narnia.Core.Repositories;
using NexusLabs.Narnia.Core.Services;

namespace NexusLabs.Narnia.Web.Tests;

public sealed class WindowSnapshotterHostedServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task RunsSnapshot_WhenEnabled()
    {
        var snapshotter = new Mock<ITerminalWindowSnapshotter>();
        var called = new TaskCompletionSource();
        snapshotter
            .Setup(s => s.SnapshotAsync(It.IsAny<DateTimeOffset>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                called.TrySetResult();
                return ValueTask.CompletedTask;
            });

        var settings = new Mock<INarniaSettingsRepository>();
        settings.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var service = new WindowSnapshotterHostedService(
            snapshotter.Object,
            settings.Object,
            new NarniaOptions { SnapshotterEnabled = true, SnapshotterIntervalSeconds = 5 },
            NullLogger<WindowSnapshotterHostedService>.Instance);

        await service.StartAsync(Ct);
        var winner = await Task.WhenAny(called.Task, Task.Delay(2000, Ct));
        await service.StopAsync(Ct);

        Assert.Same(called.Task, winner);
        snapshotter.Verify(
            s => s.SnapshotAsync(It.IsAny<DateTimeOffset>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task SkipsSnapshot_WhenDisabledBySetting()
    {
        var snapshotter = new Mock<ITerminalWindowSnapshotter>();

        var settings = new Mock<INarniaSettingsRepository>();
        settings.Setup(s => s.GetAsync(SnapshotterSettingKeys.Enabled, It.IsAny<CancellationToken>()))
            .ReturnsAsync("false");
        settings.Setup(s => s.GetAsync(SnapshotterSettingKeys.IntervalSeconds, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var service = new WindowSnapshotterHostedService(
            snapshotter.Object,
            settings.Object,
            new NarniaOptions { SnapshotterEnabled = true },
            NullLogger<WindowSnapshotterHostedService>.Instance);

        await service.StartAsync(Ct);
        await Task.Delay(300, Ct);
        await service.StopAsync(Ct);

        snapshotter.Verify(
            s => s.SnapshotAsync(It.IsAny<DateTimeOffset>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
