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
    int CheckpointCount = 0)
{
    /// <summary>
    /// Gets whether the session is marked as a favorite in Narnia.
    /// </summary>
    public bool IsFavorite { get; init; }
}
