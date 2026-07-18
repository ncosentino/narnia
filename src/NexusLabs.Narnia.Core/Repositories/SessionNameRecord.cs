namespace NexusLabs.Narnia.Core.Repositories;

internal sealed record SessionNameRecord(
    string SessionId,
    string? Name,
    DateTimeOffset UpdatedAt);
