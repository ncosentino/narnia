using System.IO.Abstractions;
using NexusLabs.Narnia.Core.Models;
using NexusLabs.Narnia.Core.Repositories;

namespace NexusLabs.Narnia.Core.Services;

/// <summary>
/// Default <see cref="ISessionWorktreeAdvisor"/>. Resolves the directory a session would launch
/// into, enumerates the sibling worktrees of that repository, and reports where the session's
/// recorded branch override disagrees with what Git actually shows.
/// </summary>
/// <remarks>
/// The branch override is display metadata: it never selects a launch directory, and this advisor
/// never checks a branch out. Doing so would be actively wrong — Git refuses to check out a branch
/// that another worktree already holds, and a checkout would rewrite a tree that a live agent may be
/// working in. Advisories therefore point at the worktree that <em>already</em> holds the branch and
/// let the user adopt it explicitly.
/// </remarks>
public sealed class SessionWorktreeAdvisor(
    ISessionRepository sessionRepository,
    ISessionOverridesRepository overridesRepository,
    IWorkspaceReader workspaceReader,
    IGitWorktreeReader worktreeReader,
    IFileSystem fileSystem) : ISessionWorktreeAdvisor
{
    /// <inheritdoc />
    public async ValueTask<SessionWorktreeAdvice> AdviseAsync(string sessionId, CancellationToken ct)
    {
        var session = await sessionRepository.GetByIdAsync(sessionId, ct);
        var overrideRecord = await overridesRepository.GetOverrideAsync(sessionId, ct);
        var branchOverride = string.IsNullOrWhiteSpace(overrideRecord?.Branch)
            ? null
            : overrideRecord.Branch.Trim();

        var directory = ResolveDirectory(sessionId, session, overrideRecord);
        if (directory is null)
        {
            return new SessionWorktreeAdvice(
                sessionId,
                null,
                null,
                branchOverride,
                [],
                [
                    new WorktreeAdvisory(
                        WorktreeAdvisoryKind.NotARepository,
                        "No launch directory could be resolved for this session, so its worktree cannot be checked.",
                        null,
                        null),
                ]);
        }

        var inspection = await worktreeReader.ReadAsync(directory, ct);
        if (!inspection.IsRepository)
        {
            return new SessionWorktreeAdvice(
                sessionId,
                directory,
                null,
                branchOverride,
                [],
                [BuildUnavailableAdvisory(directory, inspection.Error)]);
        }

        var current = inspection.Worktrees.FirstOrDefault(
            worktree => DirectoryPaths.AreSame(worktree.Path, directory));

        return new SessionWorktreeAdvice(
            sessionId,
            directory,
            current?.Branch,
            branchOverride,
            inspection.Worktrees,
            BuildAdvisories(directory, branchOverride, current, inspection.Worktrees));
    }

    private static WorktreeAdvisory BuildUnavailableAdvisory(string directory, string? error)
    {
        // "not a git repository" is a normal state for a session working outside version control,
        // while a missing executable is an environment problem the user may want to fix.
        var isMissingGit = error?.Contains("could not be started", StringComparison.OrdinalIgnoreCase) == true;
        return isMissingGit
            ? new WorktreeAdvisory(
                WorktreeAdvisoryKind.GitUnavailable,
                $"Git could not be run, so worktrees cannot be listed. {error}",
                null,
                null)
            : new WorktreeAdvisory(
                WorktreeAdvisoryKind.NotARepository,
                $"{directory} is not inside a Git repository, so there are no worktrees to choose from.",
                null,
                null);
    }

    private static IReadOnlyList<WorktreeAdvisory> BuildAdvisories(
        string directory,
        string? branchOverride,
        GitWorktree? current,
        IReadOnlyList<GitWorktree> worktrees)
    {
        if (branchOverride is null)
            return [];

        var holder = worktrees.FirstOrDefault(
            worktree => string.Equals(worktree.Branch, branchOverride, StringComparison.Ordinal));

        if (holder is null)
        {
            return
            [
                new WorktreeAdvisory(
                    WorktreeAdvisoryKind.BranchNotCheckedOut,
                    $"The branch override '{branchOverride}' is not checked out in any worktree of this " +
                    $"repository, so it is only a label. This session launches into {directory}" +
                    (current?.Branch is null ? "." : $", which is on '{current.Branch}'."),
                    null,
                    null),
            ];
        }

        if (current is not null && DirectoryPaths.AreSame(holder.Path, current.Path))
            return [];

        return
        [
            new WorktreeAdvisory(
                WorktreeAdvisoryKind.BranchInDifferentWorktree,
                $"The branch override '{branchOverride}' is checked out at {holder.Path}, but this " +
                $"session launches into {directory}" +
                (current?.Branch is null ? "." : $", which is on '{current.Branch}'.") +
                " Adopt that worktree to keep this session on its own branch.",
                holder.Path,
                holder.Branch),
        ];
    }

    private string? ResolveDirectory(string sessionId, Session? session, SessionOverride? overrideRecord)
    {
        if (Usable(overrideRecord?.LocalPath))
            return overrideRecord!.LocalPath;
        if (Usable(session?.Cwd))
            return session!.Cwd;

        string? workspaceGitRoot = null;
        try
        {
            workspaceGitRoot = workspaceReader.ReadWorkspace(sessionId)?.GitRoot;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }

        var gitRoot = workspaceGitRoot ?? session?.GitRoot;
        return Usable(gitRoot) ? gitRoot : null;
    }

    private bool Usable(string? path) =>
        !string.IsNullOrWhiteSpace(path) && fileSystem.Directory.Exists(path);
}
