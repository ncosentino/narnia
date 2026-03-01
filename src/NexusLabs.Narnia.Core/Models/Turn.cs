namespace NexusLabs.Narnia.Core.Models;

public sealed record Turn(
    long Id,
    string SessionId,
    int TurnIndex,
    string? UserMessage,
    string? AssistantResponse,
    DateTimeOffset Timestamp);
