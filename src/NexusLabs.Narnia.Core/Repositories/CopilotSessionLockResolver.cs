using System.IO.Abstractions;
using NexusLabs.Narnia.Core.Configuration;

namespace NexusLabs.Narnia.Core.Repositories;

/// <summary>
/// Reads <c>inuse.&lt;pid&gt;.lock</c> marker files under the Copilot CLI's own session-state
/// directory to answer "which session is process X running?" — the reverse of
/// <see cref="IWorkspaceReader"/>, which answers "what workspace does session Y have?".
/// </summary>
public sealed class CopilotSessionLockResolver(NarniaOptions options, IFileSystem fileSystem)
    : ICopilotSessionLockResolver
{
    /// <inheritdoc />
    public string? ResolveSessionId(int copilotProcessId)
    {
        if (!fileSystem.Directory.Exists(options.SessionStatePath))
            return null;

        var lockFileName = $"inuse.{copilotProcessId}.lock";
        string[] matches;
        try
        {
            matches = fileSystem.Directory.GetFiles(
                options.SessionStatePath, lockFileName, SearchOption.AllDirectories);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        if (matches.Length == 0)
            return null;

        // A single agent process can hold locks in more than one session-state folder when it
        // has spawned an in-process sub-agent/background task (each gets its own session-state
        // folder while sharing the parent's OS process). The oldest folder is the top-level
        // session the user is actually looking at in this tab, not a nested sub-task.
        var sessionDir = matches
            .Select(fileSystem.Path.GetDirectoryName)
            .Where(dir => !string.IsNullOrEmpty(dir))
            .Select(dir => dir!)
            .OrderBy(dir => fileSystem.Directory.GetCreationTimeUtc(dir))
            .FirstOrDefault();

        return sessionDir is null ? null : fileSystem.Path.GetFileName(sessionDir);
    }
}
