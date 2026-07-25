namespace NexusLabs.Narnia.Core.Models;

/// <summary>Filesystem metadata for a session that supplements the database record.</summary>
public sealed record WorkspaceInfo(
    string SessionId,
    string? GitRoot,
    string[] ArtifactFiles)
{
    /// <summary>Gets the Copilot-managed session name recorded in <c>workspace.yaml</c>.</summary>
    public string? Name { get; init; }

    /// <summary>Gets whether the Copilot session name was explicitly assigned by the user.</summary>
    public bool IsUserNamed { get; init; }

    /// <summary>Gets the parent multi-context task identifier for a nested agent session.</summary>
    public string? ParentTaskId { get; init; }

    /// <summary>Gets the parent multi-context session identifier for a nested agent session.</summary>
    public string? ParentSessionId { get; init; }

    /// <summary>Gets whether Copilot recorded this workspace as a nested agent session.</summary>
    public bool IsNestedAgent =>
        !string.IsNullOrWhiteSpace(ParentTaskId) ||
        !string.IsNullOrWhiteSpace(ParentSessionId);
}
