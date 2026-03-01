namespace NexusLabs.Narnia.Core.Models;

public sealed record RepositoryStats(
    string Repository,
    int SessionCount,
    int TurnCount,
    int FilesTouched,
    DateTimeOffset LastActivity);
