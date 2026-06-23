using NexusLabs.Narnia.Core.Repositories;

namespace NexusLabs.Narnia.Core.Services;

/// <summary>
/// The built-in terminal-window source backed by the snapshotter's persisted records.
/// </summary>
public sealed class LiveTerminalWindowSource(ITerminalWindowsRepository repository) : ITerminalWindowSource
{
    /// <inheritdoc />
    public string SourceId => "live";

    /// <inheritdoc />
    public async ValueTask<TerminalWindowSnapshot> GetWindowsAsync(int closedLimit, CancellationToken ct = default)
    {
        var open = await repository.GetOpenAsync(ct);
        var closed = await repository.GetClosedAsync(closedLimit, ct);
        return new TerminalWindowSnapshot(open, closed);
    }
}
