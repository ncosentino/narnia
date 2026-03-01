namespace NexusLabs.Narnia.Core.Models;

/// <summary>Filesystem metadata for a session that supplements the database record.</summary>
public sealed record WorkspaceInfo(
    string SessionId,
    string? GitRoot,
    string[] ArtifactFiles);
