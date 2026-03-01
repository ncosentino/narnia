namespace NexusLabs.Narnia.Core.Models;

public sealed record SessionRef(
    long Id,
    string SessionId,
    string? RefType,
    string? RefValue,
    int? TurnIndex,
    DateTimeOffset CreatedAt);
