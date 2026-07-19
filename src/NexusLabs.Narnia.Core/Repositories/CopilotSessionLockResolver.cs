using System.IO.Abstractions;
using NexusLabs.Narnia.Core.Configuration;

namespace NexusLabs.Narnia.Core.Repositories;

/// <summary>
/// Reads <c>inuse.&lt;pid&gt;.lock</c> marker files under the Copilot CLI's own session-state
/// directory to answer "which session is process X running?" — the reverse of
/// <see cref="IWorkspaceReader"/>, which answers "what workspace does session Y have?".
/// </summary>
public sealed class CopilotSessionLockResolver(
    NarniaOptions options,
    IFileSystem fileSystem,
    ICopilotSessionLockReader lockReader)
    : ICopilotSessionLockResolver
{
    /// <inheritdoc />
    public string? ResolveSessionId(int copilotProcessId)
    {
        var sessionIds = lockReader.GetSessionIds(copilotProcessId);
        if (sessionIds.Count == 0)
            return null;

        // A single agent process can hold locks in more than one session-state folder when it
        // has spawned an in-process sub-agent/background task (each gets its own session-state
        // folder while sharing the parent's OS process). The oldest folder is the top-level
        // session the user is actually looking at in this tab, not a nested sub-task.
        var sessionDir = sessionIds
            .Select(sessionId => fileSystem.Path.Combine(options.SessionStatePath, sessionId))
            .OrderBy(directory => fileSystem.Directory.GetCreationTimeUtc(directory))
            .FirstOrDefault();

        return sessionDir is null ? null : fileSystem.Path.GetFileName(sessionDir);
    }
}
