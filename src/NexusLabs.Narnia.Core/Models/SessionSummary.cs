namespace NexusLabs.Narnia.Core.Models;

public sealed record SessionSummary(
    string Id,
    string? Cwd,
    string? Repository,
    string? Branch,
    string? Summary,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int TurnCount,
    int CheckpointCount);
