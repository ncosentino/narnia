using NexusLabs.Narnia.Core.Models;

namespace NexusLabs.Narnia.Core.Repositories;

/// <summary>
/// Persists user-curated work collections and their explicit session memberships in Narnia's settings database.
/// </summary>
public interface IWorkCollectionsRepository
{
    /// <summary>Returns every collection ordered alphabetically by name.</summary>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>All collections with their explicit members.</returns>
    ValueTask<IReadOnlyList<WorkCollection>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Returns a collection by identifier.</summary>
    /// <param name="id">The collection identifier.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>The collection, or <c>null</c> when it does not exist.</returns>
    ValueTask<WorkCollection?> GetByIdAsync(string id, CancellationToken ct = default);

    /// <summary>Returns every collection containing the requested session.</summary>
    /// <param name="sessionId">The Copilot session identifier.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>Matching collections ordered alphabetically by name.</returns>
    ValueTask<IReadOnlyList<WorkCollection>> GetBySessionIdAsync(
        string sessionId,
        CancellationToken ct = default);

    /// <summary>Creates a collection, optionally with initial explicit session memberships.</summary>
    /// <param name="name">The collection name, which is trimmed and matched case-insensitively.</param>
    /// <param name="sessionIds">Initial session identifiers. Blank and duplicate identifiers are ignored.</param>
    /// <param name="now">Timestamp used for creation, update, and initial membership times.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>The created collection.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="name"/> is blank.</exception>
    /// <exception cref="WorkCollectionNameConflictException">Thrown when the name is already in use.</exception>
    ValueTask<WorkCollection> CreateAsync(
        string name,
        IReadOnlyCollection<string> sessionIds,
        DateTimeOffset now,
        CancellationToken ct = default);

    /// <summary>Renames an existing collection.</summary>
    /// <param name="id">The collection identifier.</param>
    /// <param name="name">The new name, which is trimmed and matched case-insensitively.</param>
    /// <param name="now">Timestamp used for the collection update.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns><c>true</c> when the collection was renamed; otherwise <c>false</c>.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="name"/> is blank.</exception>
    /// <exception cref="WorkCollectionNameConflictException">Thrown when the name is already in use.</exception>
    ValueTask<bool> RenameAsync(
        string id,
        string name,
        DateTimeOffset now,
        CancellationToken ct = default);

    /// <summary>Adds explicit session memberships without disturbing existing members.</summary>
    /// <param name="id">The collection identifier.</param>
    /// <param name="sessionIds">Session identifiers to add. Blank and duplicate identifiers are ignored.</param>
    /// <param name="now">Timestamp used for new memberships and the collection update.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>The number of memberships added, or <c>null</c> when the collection does not exist.</returns>
    ValueTask<int?> AddSessionsAsync(
        string id,
        IReadOnlyCollection<string> sessionIds,
        DateTimeOffset now,
        CancellationToken ct = default);

    /// <summary>Removes explicit session memberships without affecting the sessions themselves.</summary>
    /// <param name="id">The collection identifier.</param>
    /// <param name="sessionIds">Session identifiers to remove. Blank and duplicate identifiers are ignored.</param>
    /// <param name="now">Timestamp used for the collection update when membership changes.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>The number of memberships removed, or <c>null</c> when the collection does not exist.</returns>
    ValueTask<int?> RemoveSessionsAsync(
        string id,
        IReadOnlyCollection<string> sessionIds,
        DateTimeOffset now,
        CancellationToken ct = default);

    /// <summary>Deletes a collection and its memberships without affecting any sessions.</summary>
    /// <param name="id">The collection identifier.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns><c>true</c> when a collection was deleted; otherwise <c>false</c>.</returns>
    ValueTask<bool> DeleteAsync(string id, CancellationToken ct = default);
}
