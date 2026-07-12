namespace NexusLabs.Narnia.Core.Models;

public sealed record FileHistoryEntry(
    string SessionId,
    string? Summary,
    string? ToolName,
    DateTimeOffset? FirstSeenAt,
    string? CheckpointOverview)
{
    /// <summary>
    /// Gets whether the associated session is marked as a favorite in Narnia.
    /// </summary>
    public bool IsFavorite { get; init; }

    /// <summary>
    /// Gets the exact path string recorded for this session.
    /// </summary>
    public string? RecordedPath { get; init; }

    /// <summary>
    /// Gets the original summary recorded in the Copilot session store when a Narnia override is active.
    /// </summary>
    public string? RecordedSummary { get; init; }
}
