using NexusLabs.Narnia.Core.Models;

namespace NexusLabs.Narnia.Core.Services;

/// <summary>
/// Answers "which worktree should this session launch into, and does its recorded intent still
/// match reality?" — the data behind the worktree picker and the override coherence warnings.
/// </summary>
public interface ISessionWorktreeAdvisor
{
    /// <summary>Builds worktree choices and advisories for a session.</summary>
    /// <param name="sessionId">The session being inspected.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// Advice for the session. A session with no resolvable directory still returns a result, with
    /// no worktrees and an explanatory advisory.
    /// </returns>
    ValueTask<SessionWorktreeAdvice> AdviseAsync(string sessionId, CancellationToken ct);
}
