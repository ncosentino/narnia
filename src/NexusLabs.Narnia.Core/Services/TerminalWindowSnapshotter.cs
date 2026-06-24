using NexusLabs.Narnia.Core.Models;
using NexusLabs.Narnia.Core.Repositories;

namespace NexusLabs.Narnia.Core.Services;

/// <summary>
/// Reconciles detected live terminal windows against persisted state: open windows are
/// upserted (keyed by terminal process id), windows that have vanished since the previous
/// pass are closed, and closed history is pruned to the retention bound.
/// </summary>
public sealed class TerminalWindowSnapshotter(
    ILiveWindowDetector detector,
    ITerminalWindowsRepository repository) : ITerminalWindowSnapshotter
{
    /// <inheritdoc />
    public async ValueTask SnapshotAsync(DateTimeOffset now, int retentionCount, CancellationToken ct = default)
    {
        var detected = detector.DetectWindows();
        var openBefore = await repository.GetOpenAsync(ct);

        var detectedPids = new HashSet<int>();
        foreach (var window in detected)
        {
            detectedPids.Add(window.TerminalProcessId);

            var tabs = window.Tabs
                .Select(tab => new TerminalWindowTab(tab.SessionId, tab.Order, tab.Directory))
                .ToList();
            var compositionKey = TerminalWindowComposition.Key(window.Tabs.Select(tab => tab.SessionId));

            await repository.UpsertOpenAsync(window.TerminalProcessId, compositionKey, tabs, now, ct);
        }

        foreach (var window in openBefore)
        {
            if (window.TerminalProcessId is { } pid && !detectedPids.Contains(pid))
                await repository.CloseAsync(window.Id, now, ct);
        }

        await repository.PruneClosedAsync(retentionCount, ct);
    }
}
