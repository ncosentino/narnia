using NexusLabs.Narnia.Core.Models;

namespace NexusLabs.Narnia.Core.Services;

/// <summary>Builds the enriched read model used by the Session Storage experience.</summary>
public interface ISessionStorageService
{
    /// <summary>Gets cached storage, session metadata, protections, activity, and history.</summary>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>The complete Session Storage read model.</returns>
    ValueTask<SessionStorageDashboard> GetDashboardAsync(CancellationToken ct);
}
