namespace NexusLabs.Narnia.Core.Models;

public sealed record SearchResult(
    string SessionId,
    string? SourceType,
    string? SourceId,
    string? Content,
    double Score);
