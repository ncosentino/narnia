namespace NexusLabs.Narnia.Core.Models;

/// <summary>Lightweight indexed metadata used to enrich local storage measurements.</summary>
/// <param name="SessionId">Copilot session identifier.</param>
/// <param name="WorkingDirectory">Recorded working directory.</param>
/// <param name="Repository">Recorded remote repository.</param>
/// <param name="Summary">Recorded Copilot session name or summary.</param>
/// <param name="CreatedAt">When the indexed session was created.</param>
/// <param name="UpdatedAt">When the indexed session was most recently updated.</param>
public sealed record SessionStorageMetadata(
    string SessionId,
    string? WorkingDirectory,
    string? Repository,
    string? Summary,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
