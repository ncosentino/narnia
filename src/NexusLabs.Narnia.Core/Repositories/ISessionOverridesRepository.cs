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

    /// <summary>
    /// Creates or updates user-editable session metadata without changing archive or favorite state.
    /// </summary>
    /// <param name="sessionOverride">Metadata values to persist.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    ValueTask UpsertMetadataAsync(SessionOverride sessionOverride, CancellationToken ct = default);

    /// <summary>
    /// Clears user-editable session metadata without changing archive or favorite state.
    /// </summary>
    /// <param name="sessionId">Session whose metadata should be cleared.</param>
    /// <param name="updatedAt">Timestamp recorded for the change.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    ValueTask ResetMetadataAsync(string sessionId, DateTimeOffset updatedAt, CancellationToken ct = default);

    /// <summary>
    /// Atomically updates a session's archive state.
    /// </summary>
    /// <param name="sessionId">Session to update.</param>
    /// <param name="isArchived">New archive state.</param>
    /// <param name="updatedAt">Timestamp recorded for the change.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    ValueTask SetArchivedAsync(string sessionId, bool isArchived, DateTimeOffset updatedAt, CancellationToken ct = default);

    /// <summary>
    /// Atomically updates a session's favorite state.
    /// </summary>
    /// <param name="sessionId">Session to update.</param>
    /// <param name="isFavorite">New favorite state.</param>
    /// <param name="updatedAt">Timestamp recorded for the change.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    ValueTask SetFavoriteAsync(string sessionId, bool isFavorite, DateTimeOffset updatedAt, CancellationToken ct = default);

    ValueTask<HashSet<string>> GetArchivedSessionIdsAsync(CancellationToken ct = default);
}
