namespace NexusLabs.Narnia.Core.Models;

/// <summary>
/// A user-curated, named set of Copilot sessions that can be reopened together — for example a
/// group of sessions you always want to restore after a crash or machine restart. A group is
/// stored as an ordered list of session ids; each session's metadata (title, directory) is
/// resolved fresh at reopen time rather than captured.
/// </summary>
/// <param name="Id">Narnia-assigned stable identifier.</param>
/// <param name="Name">User-supplied display name.</param>
/// <param name="CreatedAt">When the group was created.</param>
/// <param name="UpdatedAt">When the group's name or membership last changed.</param>
/// <param name="Members">The group's member sessions, in member order.</param>
public sealed record SessionGroup(
    string Id,
    string Name,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<SessionGroupMember> Members);

/// <summary>A single Copilot session belonging to a <see cref="SessionGroup"/>.</summary>
/// <param name="SessionId">The Copilot session id to resume.</param>
/// <param name="MemberOrder">Zero-based position of the session within its group.</param>
public sealed record SessionGroupMember(
    string SessionId,
    int MemberOrder);
