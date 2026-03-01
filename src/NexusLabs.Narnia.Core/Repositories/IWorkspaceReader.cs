using NexusLabs.Narnia.Core.Models;

namespace NexusLabs.Narnia.Core.Repositories;

public interface IWorkspaceReader
{
    WorkspaceInfo ReadWorkspace(string sessionId);
}
