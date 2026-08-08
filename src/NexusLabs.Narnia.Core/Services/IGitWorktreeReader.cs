using NexusLabs.Narnia.Core.Models;

namespace NexusLabs.Narnia.Core.Services;

/// <summary>Enumerates the Git worktrees attached to the repository containing a directory.</summary>
public interface IGitWorktreeReader
{
    /// <summary>Lists every worktree of the repository that owns <paramref name="directory"/>.</summary>
    /// <param name="directory">Any directory inside the repository; need not be a worktree root.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// The enumeration result. When the directory is not a repository (or Git is unavailable),
    /// <see cref="GitWorktreeInspection.IsRepository"/> is <c>false</c> and the reason is carried in
    /// <see cref="GitWorktreeInspection.Error"/> — this is never signalled by throwing.
    /// </returns>
    ValueTask<GitWorktreeInspection> ReadAsync(string directory, CancellationToken ct);

    /// <summary>Determines whether a local branch exists in the repository owning a directory.</summary>
    /// <param name="directory">Any directory inside the repository.</param>
    /// <param name="branch">Short branch name, for example <c>main</c> or <c>feature/x</c>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// Whether the branch was found. A failed or timed-out check yields
    /// <see cref="GitBranchPresence.Unknown"/> rather than <see cref="GitBranchPresence.Missing"/>,
    /// so that a caller can decline to warn without positive evidence.
    /// </returns>
    ValueTask<GitBranchPresence> FindBranchAsync(string directory, string branch, CancellationToken ct);
}
