namespace NexusLabs.Narnia.Core.Models;

/// <summary>A bounded, read-only projection of one raw Copilot user or steering event.</summary>
/// <param name="Type">Recorded event type.</param>
/// <param name="Timestamp">Event timestamp when the event exposes one.</param>
/// <param name="Message">Recorded message content.</param>
/// <param name="Source">Event source when Copilot identifies one.</param>
public sealed record RawSessionEvent(
    string Type,
    DateTimeOffset? Timestamp,
    string Message,
    string? Source)
{
    /// <summary>Gets whether the event represents direction entered directly by the user.</summary>
    public bool IsDirectUserMessage =>
        string.Equals(Type, "user.message", StringComparison.OrdinalIgnoreCase) &&
        string.IsNullOrWhiteSpace(Source);
}

/// <summary>Recent raw event evidence read without modifying Copilot-owned files.</summary>
/// <param name="DirectUserMessages">Newest direct user messages retained from the full stream.</param>
/// <param name="SteeringMessages">Newest agent or steering messages retained from the full stream.</param>
/// <param name="LatestMessageTimestamp">Newest retained message timestamp.</param>
/// <param name="Truncated">Whether bounded retention or malformed records omitted evidence.</param>
/// <param name="IndexMayBeStale">Whether direct raw user direction is absent from indexed turns.</param>
public sealed record RawSessionEventTail(
    IReadOnlyList<RawSessionEvent> DirectUserMessages,
    IReadOnlyList<RawSessionEvent> SteeringMessages,
    DateTimeOffset? LatestMessageTimestamp,
    bool Truncated,
    bool IndexMayBeStale)
{
    public static RawSessionEventTail Empty { get; } = new([], [], null, false, false);
}
