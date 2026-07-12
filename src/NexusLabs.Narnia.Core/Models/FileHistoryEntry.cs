namespace NexusLabs.Narnia.Core.Models;

public sealed record FileHistoryEntry(
    string SessionId,
    string? Summary,
    string? ToolName,
    DateTimeOffset FirstSeenAt,
    string? CheckpointOverview)
{
    /// <summary>
    /// Gets whether the associated session is marked as a favorite in Narnia.
    /// </summary>
    public bool IsFavorite { get; init; }
}
