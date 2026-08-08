using System.IO.Abstractions;
using NexusLabs.Narnia.Core.Models;

namespace NexusLabs.Narnia.Core.Services;

/// <summary>
/// Default <see cref="IGitWorktreeReader"/>, backed by <c>git worktree list --porcelain</c>.
/// Output handling lives in <see cref="GitWorktreePorcelain"/>.
/// </summary>
public sealed class GitWorktreeReader(IFileSystem fileSystem) : IGitWorktreeReader
{
    /// <inheritdoc />
    public async ValueTask<GitWorktreeInspection> ReadAsync(string directory, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return new GitWorktreeInspection(
                false, [], "No directory to inspect.", GitWorktreeFailure.DirectoryUnavailable);
        }
        if (!fileSystem.Directory.Exists(directory))
        {
            return new GitWorktreeInspection(
                false, [], $"Directory not found: {directory}", GitWorktreeFailure.DirectoryUnavailable);
        }

        var result = await GitProcessRunner.RunAsync(
            directory,
            ["worktree", "list", "--porcelain"],
            GitProcessRunner.DefaultTimeout,
            ct);

        if (!result.Started)
        {
            return new GitWorktreeInspection(
                false, [], $"Git could not be started: {result.Error}", GitWorktreeFailure.GitNotAvailable);
        }
        if (result.TimedOut)
        {
            return new GitWorktreeInspection(
                false, [], "Git timed out listing worktrees.", GitWorktreeFailure.TimedOut);
        }
        if (result.ExitCode != 0)
        {
            var reason = string.IsNullOrWhiteSpace(result.Error)
                ? $"Git exited with code {result.ExitCode}."
                : result.Error;
            return new GitWorktreeInspection(false, [], reason, GitWorktreeFailure.NotARepository);
        }

        return new GitWorktreeInspection(
            true,
            GitWorktreePorcelain.Parse(result.Output, fileSystem.Directory.Exists),
            null);
    }

    /// <inheritdoc />
    public async ValueTask<GitBranchPresence> FindBranchAsync(
        string directory,
        string branch,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(directory) ||
            string.IsNullOrWhiteSpace(branch) ||
            !fileSystem.Directory.Exists(directory))
        {
            return GitBranchPresence.Unknown;
        }

        // A single ref lookup, not an enumeration: repositories here routinely carry hundreds of
        // local branches, and this runs on a page load. `--verify --quiet` exits 1 with no output
        // when the ref does not resolve, which is the "missing" answer rather than an error.
        var result = await GitProcessRunner.RunAsync(
            directory,
            ["rev-parse", "--verify", "--quiet", $"refs/heads/{branch}"],
            GitProcessRunner.DefaultTimeout,
            ct);

        if (!result.Started || result.TimedOut)
            return GitBranchPresence.Unknown;

        return result.ExitCode == 0 ? GitBranchPresence.Exists : GitBranchPresence.Missing;
    }
}
