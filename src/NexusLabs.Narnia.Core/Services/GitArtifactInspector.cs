using System.ComponentModel;
using System.Diagnostics;
using System.IO.Abstractions;
using System.Security;
using NexusLabs.Narnia.Core.Models;

namespace NexusLabs.Narnia.Core.Services;

/// <summary>Performs bounded, read-only Git checks for repositories stored in session artifacts.</summary>
public sealed class GitArtifactInspector(IFileSystem fileSystem) : IGitArtifactInspector
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(5);

    /// <inheritdoc />
    public async ValueTask<GitArtifactInspection> InspectAsync(
        string sessionDirectory,
        CancellationToken ct)
    {
        var reasons = new List<string>();
        var repositoryRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<string>();
        pending.Push(sessionDirectory);

        while (pending.TryPop(out var directory))
        {
            ct.ThrowIfCancellationRequested();
            IEnumerable<string> entries;
            try
            {
                entries = fileSystem.Directory.EnumerateFileSystemEntries(directory);
            }
            catch (Exception exception) when (IsFilesystemException(exception))
            {
                reasons.Add($"Git safety scan could not read {directory}: {exception.Message}");
                continue;
            }

            try
            {
                foreach (var entry in entries)
                {
                    ct.ThrowIfCancellationRequested();
                    FileAttributes attributes;
                    try
                    {
                        attributes = fileSystem.File.GetAttributes(entry);
                    }
                    catch (Exception exception) when (IsFilesystemException(exception))
                    {
                        reasons.Add($"Git safety scan could not inspect {entry}: {exception.Message}");
                        continue;
                    }

                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        reasons.Add($"Session contains a reparse point: {entry}");
                        continue;
                    }

                    var isDirectory = (attributes & FileAttributes.Directory) != 0;
                    if (!string.Equals(
                            fileSystem.Path.GetFileName(entry),
                            ".git",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        if (isDirectory)
                            pending.Push(entry);
                        continue;
                    }

                    if (!isDirectory)
                    {
                        reasons.Add(
                            $"Session contains a linked Git worktree at {fileSystem.Path.GetDirectoryName(entry)}.");
                        continue;
                    }

                    var repositoryRoot = fileSystem.Path.GetDirectoryName(entry);
                    if (!string.IsNullOrWhiteSpace(repositoryRoot))
                        repositoryRoots.Add(repositoryRoot);
                }
            }
            catch (Exception exception) when (IsFilesystemException(exception))
            {
                reasons.Add($"Git safety scan failed in {directory}: {exception.Message}");
            }
        }

        foreach (var repositoryRoot in repositoryRoots)
            await InspectRepositoryAsync(repositoryRoot, reasons, ct);

        return new GitArtifactInspection(reasons.Count == 0, reasons);
    }

    private static async Task InspectRepositoryAsync(
        string repositoryRoot,
        List<string> reasons,
        CancellationToken ct)
    {
        var status = await RunGitAsync(
            repositoryRoot,
            ["status", "--porcelain=v1", "--untracked-files=all"],
            ct);
        if (!status.Started)
        {
            reasons.Add($"Git could not inspect {repositoryRoot}: {status.Error}");
            return;
        }
        if (status.TimedOut)
        {
            reasons.Add($"Git status timed out for {repositoryRoot}.");
            return;
        }
        if (status.ExitCode != 0)
        {
            reasons.Add($"Git status failed for {repositoryRoot}: {status.Error}");
            return;
        }
        if (!string.IsNullOrWhiteSpace(status.Output))
        {
            reasons.Add($"Git repository has modified or untracked files: {repositoryRoot}");
            return;
        }

        var upstream = await RunGitAsync(
            repositoryRoot,
            ["rev-parse", "--abbrev-ref", "--symbolic-full-name", "@{u}"],
            ct);
        if (!upstream.Started || upstream.TimedOut || upstream.ExitCode != 0)
        {
            reasons.Add($"Git repository has no verifiable upstream branch: {repositoryRoot}");
            return;
        }

        var divergence = await RunGitAsync(
            repositoryRoot,
            ["rev-list", "--left-right", "--count", "HEAD...@{u}"],
            ct);
        if (!divergence.Started || divergence.TimedOut || divergence.ExitCode != 0)
        {
            reasons.Add($"Git could not verify pushed commits for {repositoryRoot}.");
            return;
        }

        var counts = divergence.Output
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (counts.Length != 2 || !int.TryParse(counts[0], out var ahead))
        {
            reasons.Add($"Git returned an unexpected divergence result for {repositoryRoot}.");
            return;
        }
        if (ahead > 0)
            reasons.Add($"Git repository has {ahead} unpushed commit(s): {repositoryRoot}");
    }

    private static async Task<GitCommandResult> RunGitAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        CancellationToken ct)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };
        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);

        try
        {
            if (!process.Start())
                return new GitCommandResult(false, false, -1, "", "Git did not start.");
        }
        catch (Win32Exception exception)
        {
            return new GitCommandResult(false, false, -1, "", exception.Message);
        }

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(CommandTimeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            if (!process.HasExited)
                process.Kill(true);
            await process.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(outputTask, errorTask);
            return new GitCommandResult(true, true, -1, "", "Timed out.");
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
                process.Kill(true);
            await process.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(outputTask, errorTask);
            throw;
        }

        return new GitCommandResult(
            true,
            false,
            process.ExitCode,
            await outputTask,
            (await errorTask).Trim());
    }

    private static bool IsFilesystemException(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or SecurityException;

    private sealed record GitCommandResult(
        bool Started,
        bool TimedOut,
        int ExitCode,
        string Output,
        string Error);
}
