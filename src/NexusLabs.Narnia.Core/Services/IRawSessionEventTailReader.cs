using NexusLabs.Narnia.Core.Models;

namespace NexusLabs.Narnia.Core.Services;

/// <summary>Reads bounded raw event evidence from a Copilot session directory.</summary>
public interface IRawSessionEventTailReader
{
    /// <summary>Reads recent raw events and compares their freshness with the indexed session.</summary>
    /// <param name="session">Chronicle-indexed session metadata.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>Bounded raw event evidence.</returns>
    ValueTask<RawSessionEventTail> ReadAsync(Session session, CancellationToken ct = default);
}
