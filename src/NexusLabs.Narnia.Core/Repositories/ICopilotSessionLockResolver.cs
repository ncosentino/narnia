namespace NexusLabs.Narnia.Core.Repositories;

/// <summary>
/// Resolves a running Copilot CLI agent process id to its session id via the CLI's own
/// <c>~/.copilot/session-state/&lt;sessionId&gt;/inuse.&lt;pid&gt;.lock</c> marker.
/// </summary>
/// <remarks>
/// This is the only available signal for a session that was started fresh (plain
/// <c>copilot</c>, no <c>--resume=&lt;guid&gt;</c>) — its process chain carries no session id
/// anywhere in a command line for <see cref="Services.ILiveWindowDetector"/> to regex-match.
/// </remarks>
public interface ICopilotSessionLockResolver
{
    /// <summary>
    /// Finds the session id whose lock file names the given Copilot agent process id, or
    /// <see langword="null"/> if none matches.
    /// </summary>
    string? ResolveSessionId(int copilotProcessId);
}
