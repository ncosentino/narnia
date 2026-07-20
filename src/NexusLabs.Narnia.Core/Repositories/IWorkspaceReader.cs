using NexusLabs.Narnia.Core.Models;

namespace NexusLabs.Narnia.Core.Repositories;

/// <summary>Reads supplemental Copilot session metadata without modifying Copilot-owned files.</summary>
public interface IWorkspaceReader
{
    /// <summary>Reads workspace metadata without enumerating session artifact files.</summary>
    /// <param name="sessionId">The Copilot session identifier.</param>
    /// <returns>Filesystem metadata with an empty artifact-file list when unavailable.</returns>
    WorkspaceInfo ReadMetadata(string sessionId);

    /// <summary>Reads the available workspace metadata for a session.</summary>
    /// <param name="sessionId">The Copilot session identifier.</param>
    /// <returns>Filesystem metadata, or an empty metadata record when the session state is unavailable.</returns>
    WorkspaceInfo ReadWorkspace(string sessionId);
}
