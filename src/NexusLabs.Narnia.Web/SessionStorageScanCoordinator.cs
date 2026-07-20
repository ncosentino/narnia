using Microsoft.Data.Sqlite;
using NexusLabs.Narnia.Core.Services;

namespace NexusLabs.Narnia.Web;

/// <summary>Runs cached session-storage scans initially, periodically, and on demand.</summary>
public sealed class SessionStorageScanCoordinator(
    ISessionStorageScanner scanner,
    TimeProvider timeProvider,
    ILogger<SessionStorageScanCoordinator> logger)
    : IHostedService, ISessionStorageScanCoordinator
{
    private static readonly TimeSpan ScanInterval = TimeSpan.FromHours(6);
    private readonly Lock _sync = new();
    private readonly SemaphoreSlim _requests = new(0, 1);
    private CancellationTokenSource? _cts;
    private Task? _scanLoop;
    private Task? _timerLoop;
    private bool _queued;
    private bool _running;
    private SessionStorageScanProgress _progress =
        new("idle", null, null, 0, 0, null);

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = new CancellationTokenSource();
        _scanLoop = RunScansAsync(_cts.Token);
        _timerLoop = QueuePeriodicScansAsync(_cts.Token);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        var cts = _cts;
        var scanLoop = _scanLoop;
        var timerLoop = _timerLoop;
        if (cts is null || scanLoop is null || timerLoop is null)
            return;

        _cts = null;
        _scanLoop = null;
        _timerLoop = null;
        await cts.CancelAsync();
        try
        {
            await Task.WhenAll(scanLoop, timerLoop).WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            cts.Dispose();
        }
    }

    /// <inheritdoc />
    public SessionStorageScanProgress GetProgress()
    {
        lock (_sync)
            return _progress;
    }

    /// <inheritdoc />
    public bool RequestScan()
    {
        lock (_sync)
        {
            if (_running || _queued)
                return false;
            _queued = true;
            _requests.Release();
            return true;
        }
    }

    private async Task QueuePeriodicScansAsync(CancellationToken ct)
    {
        RequestScan();
        using var timer = new PeriodicTimer(ScanInterval, timeProvider);
        while (await timer.WaitForNextTickAsync(ct))
            RequestScan();
    }

    private async Task RunScansAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await _requests.WaitAsync(ct);
            var startedAt = timeProvider.GetUtcNow();
            lock (_sync)
            {
                _queued = false;
                _running = true;
                _progress = new SessionStorageScanProgress(
                    "running",
                    startedAt,
                    null,
                    0,
                    0,
                    null);
            }

            var progress = new Progress<(int Scanned, int Total)>(value =>
            {
                lock (_sync)
                {
                    _progress = _progress with
                    {
                        ScannedSessions = value.Scanned,
                        TotalSessions = value.Total,
                    };
                }
            });

            try
            {
                await scanner.ScanAsync(progress, ct);
                lock (_sync)
                {
                    _progress = _progress with
                    {
                        Status = "completed",
                        CompletedAt = timeProvider.GetUtcNow(),
                    };
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception) when (
                exception is IOException or
                    UnauthorizedAccessException or
                    SqliteException or
                    InvalidOperationException)
            {
                logger.LogWarning(exception, "Session-storage scan failed.");
                lock (_sync)
                {
                    _progress = _progress with
                    {
                        Status = "failed",
                        CompletedAt = timeProvider.GetUtcNow(),
                        Error = exception.Message,
                    };
                }
            }
            finally
            {
                lock (_sync)
                    _running = false;
            }
        }
    }
}
