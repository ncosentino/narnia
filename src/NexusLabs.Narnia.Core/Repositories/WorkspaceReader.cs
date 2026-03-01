using System.IO.Abstractions;
using NexusLabs.Narnia.Core.Configuration;
using NexusLabs.Narnia.Core.Models;

namespace NexusLabs.Narnia.Core.Repositories;

public sealed class WorkspaceReader(NarniaOptions options, IFileSystem fileSystem) : IWorkspaceReader
{
    public WorkspaceInfo ReadWorkspace(string sessionId)
    {
        var sessionDir = fileSystem.Path.Combine(options.SessionStatePath, sessionId);

        string? gitRoot = null;
        var workspacePath = fileSystem.Path.Combine(sessionDir, "workspace.yaml");
        if (fileSystem.File.Exists(workspacePath))
        {
            foreach (var line in fileSystem.File.ReadAllLines(workspacePath))
            {
                var colonIndex = line.IndexOf(':');
                if (colonIndex < 1) continue;
                var key = line[..colonIndex].Trim();
                var value = line[(colonIndex + 1)..].Trim();
                if (key == "git_root")
                {
                    gitRoot = value;
                    break;
                }
            }
        }

        var filesDir = fileSystem.Path.Combine(sessionDir, "files");
        string[] artifacts = fileSystem.Directory.Exists(filesDir)
            ? [.. fileSystem.Directory.GetFiles(filesDir)
                .Select(f => fileSystem.Path.GetFileName(f))]
            : [];

        return new WorkspaceInfo(sessionId, gitRoot, artifacts);
    }
}
