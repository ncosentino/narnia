namespace NexusLabs.Narnia.Core.Models;

/// <summary>Classifies where a recorded file hotspot lives.</summary>
public enum FileActivityKind
{
    /// <summary>The file context could not be classified.</summary>
    Unknown,

    /// <summary>A project or repository file.</summary>
    Project,

    /// <summary>A file beneath the platform temporary directory.</summary>
    Temporary,

    /// <summary>A file beneath Copilot's session-state directory.</summary>
    CopilotSessionState,

    /// <summary>A file beneath Copilot's configuration directory.</summary>
    CopilotConfiguration,

    /// <summary>A local file outside the recognized project and generated-data locations.</summary>
    Other,
}

/// <summary>
/// Summarizes a recorded file path across Copilot sessions.
/// </summary>
public sealed record HotFile(string FilePath, int SessionCount, string? LastToolName)
{
    /// <summary>Gets when the path was first recorded.</summary>
    public DateTimeOffset? FirstSeenAt { get; init; }

    /// <summary>Gets when the path was most recently recorded.</summary>
    public DateTimeOffset? LastSeenAt { get; init; }

    /// <summary>Gets the classified file context.</summary>
    public FileActivityKind ActivityKind { get; init; }

    /// <summary>Gets the repository or local-area context shown with the file.</summary>
    public string? Context { get; init; }

    /// <summary>Gets a repository- or context-relative display path when one can be derived.</summary>
    public string? DisplayPath { get; init; }

    /// <summary>Gets the dominant recorded remote repository associated with the file.</summary>
    public string? Repository { get; init; }

    /// <summary>Gets the dominant recorded working directory associated with the file.</summary>
    public string? WorkingDirectory { get; init; }

}

/// <summary>
/// Category-specific file hotspots and the complete number of identities in each category.
/// </summary>
/// <param name="ProjectFiles">Top project-file identities.</param>
/// <param name="ProjectFileCount">Total project-file identities.</param>
/// <param name="Artifacts">Top generated, configuration, session-state, and other local identities.</param>
/// <param name="ArtifactCount">Total non-project file identities.</param>
public sealed record FileHotspotSummary(
    IReadOnlyList<HotFile> ProjectFiles,
    int ProjectFileCount,
    IReadOnlyList<HotFile> Artifacts,
    int ArtifactCount);
