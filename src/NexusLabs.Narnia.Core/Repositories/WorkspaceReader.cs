using System.IO.Abstractions;
using NexusLabs.Narnia.Core.Configuration;
using NexusLabs.Narnia.Core.Models;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace NexusLabs.Narnia.Core.Repositories;

/// <summary>Reads Copilot workspace metadata and artifacts through read-only filesystem access.</summary>
public sealed class WorkspaceReader(NarniaOptions options, IFileSystem fileSystem) : IWorkspaceReader
{
    /// <inheritdoc />
    public WorkspaceInfo ReadWorkspace(string sessionId)
    {
        var sessionDir = fileSystem.Path.Combine(options.SessionStatePath, sessionId);

        string? gitRoot = null;
        string? name = null;
        var isUserNamed = false;
        var workspacePath = fileSystem.Path.Combine(sessionDir, "workspace.yaml");
        if (fileSystem.File.Exists(workspacePath))
        {
            try
            {
                var yaml = new YamlStream();
                using var reader = new StringReader(fileSystem.File.ReadAllText(workspacePath));
                yaml.Load(reader);
                if (yaml.Documents.FirstOrDefault()?.RootNode is YamlMappingNode root)
                {
                    gitRoot = ReadScalar(root, "git_root");
                    name = ReadScalar(root, "name");
                    isUserNamed = string.Equals(
                        ReadScalar(root, "user_named"),
                        "true",
                        StringComparison.OrdinalIgnoreCase);
                }
            }
            catch (YamlException)
            {
            }
        }

        var filesDir = fileSystem.Path.Combine(sessionDir, "files");
        string[] artifacts = fileSystem.Directory.Exists(filesDir)
            ? [.. fileSystem.Directory.GetFiles(filesDir)
                .Select(f => fileSystem.Path.GetFileName(f))]
            : [];

        return new WorkspaceInfo(sessionId, gitRoot, artifacts)
        {
            Name = name,
            IsUserNamed = isUserNamed,
        };
    }

    private static string? ReadScalar(YamlMappingNode root, string key) =>
        root.Children.TryGetValue(new YamlScalarNode(key), out var value)
        && value is YamlScalarNode scalar
            ? scalar.Value
            : null;
}
