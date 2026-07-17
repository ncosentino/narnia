namespace NexusLabs.Narnia.Core.Models;

/// <summary>
/// A user-curated logical area of work containing explicit Copilot session memberships.
/// Collections organize related sessions without prescribing launch order or terminal layout.
/// </summary>
/// <param name="Id">Narnia-assigned stable identifier.</param>
/// <param name="Name">Case-insensitively unique display name.</param>
/// <param name="CreatedAt">When the collection was created.</param>
/// <param name="UpdatedAt">When the collection name or membership last changed.</param>
/// <param name="Members">Explicit session memberships in the collection.</param>
public sealed record WorkCollection(
    string Id,
    string Name,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<WorkCollectionMember> Members);

/// <summary>
/// An explicit Copilot session membership in a <see cref="WorkCollection"/>.
/// </summary>
/// <param name="SessionId">The Copilot session identifier.</param>
/// <param name="AddedAt">When the session was added to the collection.</param>
public sealed record WorkCollectionMember(
    string SessionId,
    DateTimeOffset AddedAt);
