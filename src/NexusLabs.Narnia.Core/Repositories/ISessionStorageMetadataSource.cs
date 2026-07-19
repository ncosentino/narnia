using NexusLabs.Narnia.Core.Models;

namespace NexusLabs.Narnia.Core.Repositories;

/// <summary>Reads lightweight indexed metadata without aggregate turn or checkpoint queries.</summary>
public interface ISessionStorageMetadataSource
{
    /// <summary>Lists every indexed session using only storage-page metadata columns.</summary>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>Indexed session metadata ordered by most recent activity.</returns>
    ValueTask<IReadOnlyList<SessionStorageMetadata>> ListAsync(CancellationToken ct);
}
