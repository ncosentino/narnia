using NexusLabs.Narnia.Core.Models;

namespace NexusLabs.Narnia.Core.Repositories;

/// <summary>Reads Copilot-recorded session metadata without applying Narnia display overrides.</summary>
public interface IRecordedSessionRepository
{
    /// <summary>Lists recently updated recorded session summaries.</summary>
    ValueTask<SessionSummary[]> ListRecentAsync(
        int limit = 20,
        bool includeArchived = false,
        CancellationToken ct = default);

    /// <summary>Gets recorded sessions by identifier.</summary>
    ValueTask<IReadOnlyDictionary<string, Session>> GetByIdsAsync(
        IReadOnlyCollection<string> sessionIds,
        CancellationToken ct = default);
}
