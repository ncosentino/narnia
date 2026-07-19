using System.IO.Abstractions;
using NexusLabs.Narnia.Core.Configuration;

namespace NexusLabs.Narnia.Core.Repositories;

/// <summary>Filesystem implementation for reading Copilot's process-specific session locks.</summary>
public sealed class CopilotSessionLockReader(
    NarniaOptions options,
    IFileSystem fileSystem) : ICopilotSessionLockReader
{
    /// <inheritdoc />
    public IReadOnlyList<string> GetSessionIds(int copilotProcessId)
    {
        if (!fileSystem.Directory.Exists(options.SessionStatePath))
            return [];

        string[] matches;
        try
        {
            matches = fileSystem.Directory.GetFiles(
                options.SessionStatePath,
                $"inuse.{copilotProcessId}.lock",
                SearchOption.AllDirectories);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        return matches
            .Select(fileSystem.Path.GetDirectoryName)
            .Where(directory => !string.IsNullOrWhiteSpace(directory))
            .Select(directory => fileSystem.Path.GetFileName(directory!))
            .Where(sessionId => !string.IsNullOrWhiteSpace(sessionId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
