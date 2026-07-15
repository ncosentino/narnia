namespace NexusLabs.Narnia.Core.Models;

/// <summary>Identifies the recorded provenance dimension used to group raw session activity.</summary>
public enum SessionActivitySourceKind
{
    /// <summary>Sessions grouped by recorded remote repository.</summary>
    RemoteRepository,

    /// <summary>Sessions grouped by recorded working directory.</summary>
    WorkingDirectory,

    /// <summary>Sessions with only a recorded host type.</summary>
    Host,

    /// <summary>Sessions without repository, working-directory, or host metadata.</summary>
    Unknown,
}

/// <summary>
/// Summarizes raw Copilot session records attributed to one source on a selected local date.
/// </summary>
/// <param name="Kind">The provenance dimension represented by the row.</param>
/// <param name="Label">Human-readable source label.</param>
/// <param name="Repository">Recorded remote repository when grouped by repository.</param>
/// <param name="WorkingDirectory">Recorded working directory or normalized parent directory.</param>
/// <param name="IncludesDescendants">
/// Whether the working-directory group includes generated child directories beneath
/// <paramref name="WorkingDirectory"/>.
/// </param>
/// <param name="HostType">Recorded Copilot host type, when present.</param>
/// <param name="SessionCount">Number of raw session records in the group.</param>
public sealed record SessionActivitySource(
    SessionActivitySourceKind Kind,
    string Label,
    string? Repository,
    string? WorkingDirectory,
    bool IncludesDescendants,
    string? HostType,
    int SessionCount);

/// <summary>
/// Selects the exact raw session records represented by a <see cref="SessionActivitySource"/>.
/// </summary>
/// <param name="Date">Local calendar date represented by the source row.</param>
/// <param name="Kind">Provenance dimension represented by the source row.</param>
/// <param name="Repository">Recorded repository to match.</param>
/// <param name="WorkingDirectory">Recorded directory or normalized generated-directory parent.</param>
/// <param name="IncludesGeneratedChildren">
/// Whether only direct generated child directories beneath <paramref name="WorkingDirectory"/>
/// are included.
/// </param>
/// <param name="HostType">Recorded host type to match.</param>
/// <param name="HostTypeMissing">Whether the source row represents a missing host type.</param>
public sealed record SessionActivitySourceFilter(
    DateOnly Date,
    SessionActivitySourceKind Kind,
    string? Repository,
    string? WorkingDirectory,
    bool IncludesGeneratedChildren,
    string? HostType,
    bool HostTypeMissing);
