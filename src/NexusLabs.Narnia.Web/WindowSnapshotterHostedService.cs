using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NexusLabs.Narnia.Core.Configuration;
using NexusLabs.Narnia.Core.Repositories;
using NexusLabs.Narnia.Core.Services;

namespace NexusLabs.Narnia.Web;

/// <summary>
/// Drives the terminal-window snapshotter on a timer inside the always-on web server so a
/// lost window can be reopened later. Honors a runtime <c>enabled</c> setting and a
/// configurable interval, both read from the settings database each tick.
/// </summary>
public sealed class WindowSnapshotterHostedService(
    ITerminalWindowSnapshotter snapshotter,
    INarniaSettingsRepository settings,
    NarniaOptions options,
    ILogger<WindowSnapshotterHostedService> logger) : IHostedService
{
    private const int MinimumIntervalSeconds = 5;

    private CancellationTokenSource? _cts;
    private Task? _loop;

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = new CancellationTokenSource();
        _loop = RunLoopAsync(_cts.Token);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_cts is null || _loop is null)
            return;

        await _cts.CancelAsync();
        try
        {
            await _loop.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
        }
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (await IsEnabledAsync(ct))
                    await snapshotter.SnapshotAsync(DateTimeOffset.UtcNow, await GetRetentionAsync(ct), ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Terminal-window snapshot tick failed.");
            }

            try
            {
                await Task.Delay(await GetIntervalAsync(ct), ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task<bool> IsEnabledAsync(CancellationToken ct)
    {
        var value = await settings.GetAsync(SnapshotterSettingKeys.Enabled, ct);
        return value is null
            ? options.SnapshotterEnabled
            : !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<TimeSpan> GetIntervalAsync(CancellationToken ct)
    {
        var value = await settings.GetAsync(SnapshotterSettingKeys.IntervalSeconds, ct);
        var seconds = int.TryParse(value, out var parsed) ? parsed : options.SnapshotterIntervalSeconds;
        return TimeSpan.FromSeconds(Math.Max(MinimumIntervalSeconds, seconds));
    }

    private async Task<int> GetRetentionAsync(CancellationToken ct)
    {
        var value = await settings.GetAsync(SnapshotterSettingKeys.RetentionCount, ct);
        var count = int.TryParse(value, out var parsed) ? parsed : options.SnapshotterRetentionCount;
        return Math.Max(0, count);
    }
}
