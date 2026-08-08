using System.ComponentModel;
using System.Diagnostics;

namespace NexusLabs.Narnia.Core.Services;

/// <summary>
/// Runs a bounded, read-only Git command and captures its output. Shared by the Git-backed
/// inspectors so process startup, timeout, and kill semantics exist in exactly one place.
/// </summary>
internal static class GitProcessRunner
{
    internal static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

    internal static async Task<GitCommandResult> RunAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
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
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutSource.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(timeoutSource.Token);
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
}

/// <summary>Outcome of a single Git invocation, distinguishing "never started" from "failed".</summary>
internal sealed record GitCommandResult(
    bool Started,
    bool TimedOut,
    int ExitCode,
    string Output,
    string Error);
