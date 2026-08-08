namespace NexusLabs.Narnia.Core.Models;

/// <summary>
/// A single Git worktree attached to a repository, as reported by
/// <c>git worktree list --porcelain</c>.
/// </summary>
/// <param name="Path">Absolute worktree path, normalized to the host directory separator.</param>
/// <param name="Branch">Short branch name (e.g. <c>feature/x</c>), or <c>null</c> when detached or bare.</param>
/// <param name="Head">The commit the worktree points at, or <c>null</c> for a bare repository.</param>
/// <param name="IsBare">Whether this entry is the bare repository rather than a checkout.</param>
/// <param name="IsDetached">Whether the worktree is on a detached HEAD.</param>
/// <param name="IsPrimary">Whether this is the repository's main worktree (the first entry Git reports).</param>
/// <param name="Exists">Whether the path is still present on disk (a pruned worktree may not be).</param>
public sealed record GitWorktree(
    string Path,
    string? Branch,
    string? Head,
    bool IsBare,
    bool IsDetached,
    bool IsPrimary,
    bool Exists);

/// <summary>Whether a named local branch could be found in a repository.</summary>
public enum GitBranchPresence
{
    /// <summary>The check could not be performed, so nothing is known either way.</summary>
    Unknown,

    /// <summary><c>refs/heads/&lt;branch&gt;</c> resolves.</summary>
    Exists,

    /// <summary>Git ran and reported no such branch.</summary>
    Missing,
}

/// <summary>Whether worktree enumeration failed, and why.</summary>
public enum GitWorktreeFailure
{
    /// <summary>Enumeration succeeded.</summary>
    None,

    /// <summary>Git is not installed, or could not be executed.</summary>
    GitNotAvailable,

    /// <summary>Git did not answer within the command timeout.</summary>
    TimedOut,

    /// <summary>The directory to inspect was missing or unusable.</summary>
    DirectoryUnavailable,

    /// <summary>Git ran and reported that the directory is not inside a repository.</summary>
    NotARepository,
}

/// <summary>Result of enumerating the worktrees reachable from a directory.</summary>
/// <param name="IsRepository">Whether the directory resolved to a Git repository at all.</param>
/// <param name="Worktrees">Every worktree Git reported, in Git's own order (primary first).</param>
/// <param name="Error">Why enumeration failed, when <see cref="IsRepository"/> is <c>false</c>.</param>
/// <param name="Failure">
/// Structured reason for the failure. Callers must branch on this rather than matching
/// <paramref name="Error"/> text: "we could not check" and "we checked and it is not a repository"
/// are different claims, and only the second one is safe to assert to a user.
/// </param>
public sealed record GitWorktreeInspection(
    bool IsRepository,
    IReadOnlyList<GitWorktree> Worktrees,
    string? Error,
    GitWorktreeFailure Failure = GitWorktreeFailure.None);

/// <summary>The kind of incoherence found between a session's overrides and real Git state.</summary>
public enum WorktreeAdvisoryKind
{
    /// <summary>Git is not installed, timed out, or the directory could not be inspected.</summary>
    GitUnavailable,

    /// <summary>Git ran and reported that the launch directory is not inside a Git repository.</summary>
    NotARepository,

    /// <summary>
    /// The branch override names a branch that does not exist in the repository at all, so the
    /// label can never be satisfied.
    /// </summary>
    BranchNotFound,

    /// <summary>
    /// The branch override is checked out, but in a different worktree than the one the
    /// session will launch into — the launch would silently land on the wrong branch.
    /// </summary>
    BranchInDifferentWorktree,

    /// <summary>
    /// The launch directory's actual branch differs from the branch override, and the override
    /// branch is not checked out anywhere to redirect to.
    /// </summary>
    DirectoryBranchMismatch,
}

/// <summary>A single incoherence between a session's recorded intent and observable Git state.</summary>
/// <param name="Kind">Which incoherence was found.</param>
/// <param name="Message">Human-readable explanation, safe to render directly.</param>
/// <param name="SuggestedPath">The worktree path that would resolve the advisory, when one exists.</param>
/// <param name="SuggestedBranch">The branch that pairs with <paramref name="SuggestedPath"/>.</param>
public sealed record WorktreeAdvisory(
    WorktreeAdvisoryKind Kind,
    string Message,
    string? SuggestedPath,
    string? SuggestedBranch);

/// <summary>
/// Everything the session detail page needs to offer a worktree picker and warn about an
/// incoherent override pair.
/// </summary>
/// <param name="SessionId">The session this advice was computed for.</param>
/// <param name="ResolvedDirectory">The directory this session would actually launch into today.</param>
/// <param name="ResolvedBranch">The branch that directory currently has checked out.</param>
/// <param name="BranchOverride">The branch override recorded in Narnia's settings database.</param>
/// <param name="Worktrees">Selectable worktrees for the session's repository.</param>
/// <param name="Advisories">Problems found; empty when the overrides are coherent.</param>
public sealed record SessionWorktreeAdvice(
    string SessionId,
    string? ResolvedDirectory,
    string? ResolvedBranch,
    string? BranchOverride,
    IReadOnlyList<GitWorktree> Worktrees,
    IReadOnlyList<WorktreeAdvisory> Advisories);
