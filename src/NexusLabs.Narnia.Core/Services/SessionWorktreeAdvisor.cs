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
                branchOverride is null
                    ? []
                    :
                    [
                        new WorktreeAdvisory(
                            WorktreeAdvisoryKind.NotARepository,
                            $"The branch override '{branchOverride}' cannot be checked: no launch " +
                            "directory could be resolved for this session.",
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
                branchOverride is null
                    ? []
                    : [BuildUnavailableAdvisory(directory, branchOverride, inspection)]);
        }

        var current = inspection.Worktrees.FirstOrDefault(
            worktree => DirectoryPaths.AreSame(worktree.Path, directory));

        return new SessionWorktreeAdvice(
            sessionId,
            directory,
            current?.Branch,
            branchOverride,
            inspection.Worktrees,
            await BuildAdvisoriesAsync(directory, branchOverride, current, inspection.Worktrees, ct));
    }

    // Only a non-zero Git exit proves the directory is not a repository. A missing executable, a
    // timeout, or a vanished directory means the check never ran — asserting "not a repository" in
    // those cases states a falsehood as fact, and would tell a user to stop looking for exactly the
    // misconfiguration this advisor exists to surface.
    //
    // Every branch here is reached only when a branch override exists. Working outside version
    // control is an ordinary state (scheduled jobs run from the system directory, for instance), so
    // a session that has claimed no branch has nothing incoherent to report and stays silent.
    private static WorktreeAdvisory BuildUnavailableAdvisory(
        string directory,
        string branchOverride,
        GitWorktreeInspection inspection) =>
        inspection.Failure switch
        {
            GitWorktreeFailure.NotARepository => new WorktreeAdvisory(
                WorktreeAdvisoryKind.NotARepository,
                $"The branch override '{branchOverride}' is only a label here: {directory} is not " +
                "inside a Git repository, so there is no branch to match it against.",
                null,
                null),
            GitWorktreeFailure.TimedOut => new WorktreeAdvisory(
                WorktreeAdvisoryKind.GitUnavailable,
                $"Git did not finish listing the worktrees of {directory} in time, so the branch " +
                $"override '{branchOverride}' could not be checked. Reload to try again.",
                null,
                null),
            GitWorktreeFailure.DirectoryUnavailable => new WorktreeAdvisory(
                WorktreeAdvisoryKind.GitUnavailable,
                $"{directory} could not be inspected, so the branch override '{branchOverride}' " +
                $"could not be checked. {inspection.Error}",
                null,
                null),
            _ => new WorktreeAdvisory(
                WorktreeAdvisoryKind.GitUnavailable,
                $"Git could not be run, so the branch override '{branchOverride}' could not be " +
                $"checked. {inspection.Error}",
                null,
                null),
        };

    /// <summary>
    /// Decides whether a branch override is worth warning about.
    /// </summary>
    /// <remarks>
    /// The question that matters is whether the branch <em>exists</em>, not whether it is currently
    /// checked out. Switching branches in place is ordinary Git use, so a session labelled with a
    /// real branch that simply is not checked out right now is not a defect and must stay silent —
    /// otherwise the common case drowns the rare one. Only two situations mislead a user about where
    /// their work will land: a label naming a branch that exists nowhere, and a label naming a branch
    /// that lives in a worktree the session does not launch into.
    /// </remarks>
    private async ValueTask<IReadOnlyList<WorktreeAdvisory>> BuildAdvisoriesAsync(
        string directory,
        string? branchOverride,
        GitWorktree? current,
        IReadOnlyList<GitWorktree> worktrees,
        CancellationToken ct)
    {
        if (branchOverride is null)
            return [];

        var holder = worktrees.FirstOrDefault(
            worktree => string.Equals(worktree.Branch, branchOverride, StringComparison.Ordinal));

        if (holder is not null)
        {
            // Checked out where this session launches: the label matches reality.
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

        // Not checked out anywhere. That is unremarkable for a real branch, so only a branch Git
        // positively reports as missing is flagged; an inconclusive check says nothing.
        var presence = await worktreeReader.FindBranchAsync(directory, branchOverride, ct);
        if (presence != GitBranchPresence.Missing)
            return [];

        return
        [
            new WorktreeAdvisory(
                WorktreeAdvisoryKind.BranchNotFound,
                $"The branch override '{branchOverride}' does not name a branch that exists in this " +
                $"repository, so it is only a label. This session launches into {directory}" +
                (current?.Branch is null ? "." : $", which is on '{current.Branch}'."),
                null,
                null),
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
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Workspace metadata is a last-resort fallback. An unreadable or malformed workspace
            // file must degrade to "no directory" rather than failing the whole advisory request.
        }

        var gitRoot = workspaceGitRoot ?? session?.GitRoot;
        return Usable(gitRoot) ? gitRoot : null;
    }

    private bool Usable(string? path) =>
        !string.IsNullOrWhiteSpace(path) && fileSystem.Directory.Exists(path);
}
