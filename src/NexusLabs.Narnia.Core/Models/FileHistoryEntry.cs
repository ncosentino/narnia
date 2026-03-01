namespace NexusLabs.Narnia.Core.Models;

public sealed record FileHistoryEntry(
    string SessionId,
    string? Summary,
    string? ToolName,
    DateTimeOffset FirstSeenAt,
    string? CheckpointOverview);
