using NexusLabs.Narnia.Core.Models;

namespace NexusLabs.Narnia.Core.Services;

/// <summary>
/// Finds launches that would put two Copilot agents in one working tree.
/// </summary>
/// <remarks>
/// Two agents sharing a directory is legitimate for read-only work but hazardous for anything that
/// mutates the tree: they interleave edits, and any Git operation one runs (checkout, stash, reset)
/// silently reshapes the other's working tree. Git already makes this impossible <em>across</em>
/// worktrees — one branch can be checked out in only one worktree — so the collisions worth
/// reporting are the ones Git cannot prevent, where both sessions target the same path.
/// </remarks>
public interface ILaunchCollisionDetector
{
    /// <summary>Reports every tab that would share a directory with a live session or a sibling tab.</summary>
    /// <param name="tabs">The tabs about to be launched, with directories already resolved.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>One entry per colliding tab; empty when every tab has the directory to itself.</returns>
    ValueTask<IReadOnlyList<LaunchDirectoryCollision>> DetectAsync(
        IReadOnlyList<TerminalLaunchTab> tabs,
        CancellationToken ct);
}

/// <summary>A launch that would share a working tree with another Copilot session.</summary>
/// <param name="SessionId">The session about to be launched.</param>
/// <param name="Directory">The directory both sessions resolve to.</param>
/// <param name="OccupyingSessionId">The session already using (or about to use) that directory.</param>
/// <param name="OccupyingSessionName">Display name of the occupying session, when known.</param>
/// <param name="OccupyingIsLive">
/// <c>true</c> when the occupant is an already-running session; <c>false</c> when it is another tab
/// in this same launch request.
/// </param>
public sealed record LaunchDirectoryCollision(
    string SessionId,
    string Directory,
    string OccupyingSessionId,
    string? OccupyingSessionName,
    bool OccupyingIsLive)
{
    /// <summary>Builds a one-line explanation suitable for a confirmation prompt.</summary>
    public string Describe()
    {
        var occupant = string.IsNullOrWhiteSpace(OccupyingSessionName)
            ? OccupyingSessionId[..Math.Min(8, OccupyingSessionId.Length)]
            : OccupyingSessionName;
        return OccupyingIsLive
            ? $"{occupant} is already running in {Directory}."
            : $"{occupant} is also being launched into {Directory}.";
    }
}
