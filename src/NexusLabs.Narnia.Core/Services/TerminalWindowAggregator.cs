using NexusLabs.Narnia.Core.Models;

namespace NexusLabs.Narnia.Core.Services;

/// <summary>
/// Default aggregator: merges every registered source's windows, ordering by recency and
/// capping the combined closed list at the requested limit.
/// </summary>
public sealed class TerminalWindowAggregator(IEnumerable<ITerminalWindowSource> sources)
    : ITerminalWindowAggregator
{
    /// <inheritdoc />
    public async ValueTask<TerminalWindowSnapshot> GetWindowsAsync(int closedLimit, CancellationToken ct = default)
    {
        var open = new List<TerminalWindow>();
        var closed = new List<TerminalWindow>();

        foreach (var source in sources)
        {
            var snapshot = await source.GetWindowsAsync(closedLimit, ct);
            open.AddRange(snapshot.Open);
            closed.AddRange(snapshot.Closed);
        }

        return new TerminalWindowSnapshot(
            open.OrderByDescending(window => window.LastSeenAt).ToList(),
            closed.OrderByDescending(window => window.LastSeenAt).Take(closedLimit).ToList());
    }
}
