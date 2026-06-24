using NexusLabs.Narnia.Core.Models;

namespace NexusLabs.Narnia.Core.Repositories;

/// <summary>
/// Persists user-curated session groups (named, ordered sets of Copilot session ids) in the
/// Narnia settings database, so a whole group can be reopened together after it is lost.
/// </summary>
public interface ISessionGroupsRepository
{
    /// <summary>Returns all groups, most recently updated first, each with its members in order.</summary>
    ValueTask<IReadOnlyList<SessionGroup>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Returns a single group by id, or <c>null</c> if it does not exist.</summary>
    ValueTask<SessionGroup?> GetByIdAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// Creates a new group with the given name and members. The member order follows the order of
    /// <paramref name="sessionIds"/>; duplicate ids are collapsed to their first occurrence.
    /// </summary>
    /// <param name="name">The group's display name.</param>
    /// <param name="sessionIds">The member session ids, in the desired order.</param>
    /// <param name="now">The current timestamp, used for both created and updated.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The created group, including its assigned id and resolved members.</returns>
    ValueTask<SessionGroup> CreateAsync(
        string name,
        IReadOnlyList<string> sessionIds,
        DateTimeOffset now,
        CancellationToken ct = default);

    /// <summary>Renames a group and refreshes its updated timestamp.</summary>
    ValueTask RenameAsync(string id, string name, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>
    /// Replaces a group's entire membership with <paramref name="sessionIds"/> (in order) and
    /// refreshes its updated timestamp. Duplicate ids are collapsed to their first occurrence.
    /// </summary>
    ValueTask SetMembersAsync(
        string id,
        IReadOnlyList<string> sessionIds,
        DateTimeOffset now,
        CancellationToken ct = default);

    /// <summary>Deletes a group and its members.</summary>
    ValueTask DeleteAsync(string id, CancellationToken ct = default);
}
