using NexusLabs.Narnia.Core.Models;

namespace NexusLabs.Narnia.Core.Repositories;

public interface ISessionOverridesRepository
{
    ValueTask<SessionOverride?> GetOverrideAsync(string sessionId, CancellationToken ct = default);
    ValueTask UpsertOverrideAsync(SessionOverride sessionOverride, CancellationToken ct = default);
    ValueTask DeleteOverrideAsync(string sessionId, CancellationToken ct = default);
    ValueTask<HashSet<string>> GetArchivedSessionIdsAsync(CancellationToken ct = default);
}
