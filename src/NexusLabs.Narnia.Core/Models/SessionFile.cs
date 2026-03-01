namespace NexusLabs.Narnia.Core.Models;

public sealed record SessionFile(
    long Id,
    string SessionId,
    string? FilePath,
    string? ToolName,
    int? TurnIndex,
    DateTimeOffset FirstSeenAt);
