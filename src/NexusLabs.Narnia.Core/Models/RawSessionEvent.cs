namespace NexusLabs.Narnia.Core.Models;

/// <summary>A bounded, read-only projection of one raw Copilot event.</summary>
/// <param name="Type">Recorded event type.</param>
/// <param name="Timestamp">Event timestamp when the event exposes one.</param>
/// <param name="UserMessage">Authoritative user content when recognized.</param>
public sealed record RawSessionEvent(
    string Type,
    DateTimeOffset? Timestamp,
    string? UserMessage);

/// <summary>Recent raw event evidence read without modifying Copilot-owned files.</summary>
/// <param name="Events">Events found in the bounded tail.</param>
/// <param name="LatestTimestamp">Newest timestamp found in the tail.</param>
/// <param name="Truncated">Whether the bounded tail omitted older or partial content.</param>
/// <param name="IndexMayBeStale">Whether raw evidence is newer than the Chronicle index.</param>
public sealed record RawSessionEventTail(
    IReadOnlyList<RawSessionEvent> Events,
    DateTimeOffset? LatestTimestamp,
    bool Truncated,
    bool IndexMayBeStale)
{
    public static RawSessionEventTail Empty { get; } = new([], null, false, false);
}
