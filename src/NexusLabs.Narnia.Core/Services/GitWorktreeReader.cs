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
}
