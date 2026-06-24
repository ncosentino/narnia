using NexusLabs.Narnia.Core.Models;
using NexusLabs.Narnia.Core.Repositories;

namespace NexusLabs.Narnia.Core.Services;

/// <summary>
/// Reconciles detected live Copilot sessions against persisted state. Each session is tracked
/// as its own record (keyed by composition), because a single terminal process can host many
/// independent session windows/tabs — so grouping by terminal process id would collapse them and
/// make an individual close undetectable. Live sessions are upserted; sessions that have vanished
/// since the previous pass are closed; closed history is pruned to the retention bound.
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

        var liveCompositionKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var window in detected)
        {
            foreach (var tab in window.Tabs)
            {
                var sessionId = tab.SessionId;
                var compositionKey = TerminalWindowComposition.Key([sessionId]);
                liveCompositionKeys.Add(compositionKey);

                var sessionTabs = new List<TerminalWindowTab>
                {
                    new(sessionId, 0, tab.Directory),
                };
                await repository.UpsertOpenAsync(window.TerminalProcessId, compositionKey, sessionTabs, now, ct);
            }
        }

        foreach (var window in openBefore)
        {
            if (!liveCompositionKeys.Contains(window.CompositionKey))
                await repository.CloseAsync(window.Id, now, ct);
        }

        await repository.PruneClosedAsync(retentionCount, ct);
    }
}
