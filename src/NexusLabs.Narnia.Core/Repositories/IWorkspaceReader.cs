using NexusLabs.Narnia.Core.Models;

namespace NexusLabs.Narnia.Core.Repositories;

/// <summary>Reads supplemental Copilot session metadata without modifying Copilot-owned files.</summary>
public interface IWorkspaceReader
{
    /// <summary>Reads the available workspace metadata for a session.</summary>
    /// <param name="sessionId">The Copilot session identifier.</param>
    /// <returns>Filesystem metadata, or an empty metadata record when the session state is unavailable.</returns>
    WorkspaceInfo ReadWorkspace(string sessionId);
}
