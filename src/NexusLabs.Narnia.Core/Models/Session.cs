namespace NexusLabs.Narnia.Core.Models;

public sealed record Session(
    string Id,
    string? Cwd,
    string? Repository,
    string? Branch,
    string? Summary,
    string? GitRoot,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int TurnCount = 0,
    int CheckpointCount = 0);
