using NexusLabs.Narnia.Core.Models;

namespace NexusLabs.Narnia.Core.Services;

/// <summary>Reads bounded raw event evidence from a Copilot session directory.</summary>
public interface IRawSessionEventTailReader
{
    /// <summary>Scans the raw stream with bounded memory and compares recent direction with indexed turns.</summary>
    /// <param name="sessionId">Copilot session identifier.</param>
    /// <param name="indexedTurns">Indexed turns already selected for the recovery packet.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>Bounded raw event evidence.</returns>
    ValueTask<RawSessionEventTail> ReadAsync(
        string sessionId,
        IReadOnlyList<Turn> indexedTurns,
        CancellationToken ct = default);
}
