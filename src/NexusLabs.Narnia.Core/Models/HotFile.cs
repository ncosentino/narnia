namespace NexusLabs.Narnia.Core.Models;

/// <summary>
/// Summarizes a recorded file path across Copilot sessions.
/// </summary>
public sealed record HotFile(string FilePath, int SessionCount, string? LastToolName)
{
    /// <summary>Gets when the path was first recorded.</summary>
    public DateTimeOffset? FirstSeenAt { get; init; }

    /// <summary>Gets when the path was most recently recorded.</summary>
    public DateTimeOffset? LastSeenAt { get; init; }
}
