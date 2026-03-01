namespace NexusLabs.Narnia.Core.Models;

public sealed record ResumeSuggestion(
    SessionSummary Session,
    string? LatestCheckpointTitle,
    string? NextStepsPreview);
