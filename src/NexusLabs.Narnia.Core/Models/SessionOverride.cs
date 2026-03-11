namespace NexusLabs.Narnia.Core.Models;

public sealed record SessionOverride(
    string SessionId,
    string? DisplayName,
    string? Repository,
    string? Branch,
    string? Notes,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
