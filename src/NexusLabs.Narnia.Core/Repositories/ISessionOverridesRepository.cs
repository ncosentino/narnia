using NexusLabs.Narnia.Core.Models;

namespace NexusLabs.Narnia.Core.Repositories;

public interface ISessionOverridesRepository
{
    ValueTask<SessionOverride?> GetOverrideAsync(string sessionId, CancellationToken ct = default);

    /// <summary>
    /// Gets every saved session override keyed by session ID.
    /// </summary>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>A read-only lookup of all saved overrides.</returns>
    ValueTask<IReadOnlyDictionary<string, SessionOverride>> GetAllOverridesAsync(CancellationToken ct = default);

    ValueTask UpsertOverrideAsync(SessionOverride sessionOverride, CancellationToken ct = default);
    ValueTask DeleteOverrideAsync(string sessionId, CancellationToken ct = default);
    ValueTask<HashSet<string>> GetArchivedSessionIdsAsync(CancellationToken ct = default);
}
