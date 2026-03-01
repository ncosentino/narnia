namespace NexusLabs.Narnia.Core.Models;

public sealed record Checkpoint(
    long Id,
    string SessionId,
    int CheckpointNumber,
    string? Title,
    string? Overview,
    string? History,
    string? WorkDone,
    string? TechnicalDetails,
    string? ImportantFiles,
    string? NextSteps,
    DateTimeOffset CreatedAt);
