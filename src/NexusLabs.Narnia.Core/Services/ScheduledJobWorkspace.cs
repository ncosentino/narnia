using NexusLabs.Narnia.Core.Configuration;
using System.IO.Abstractions;
using System.Linq;

namespace NexusLabs.Narnia.Core.Services;

/// <summary>
/// Owns the on-disk workspace for Narnia-authored scheduled jobs: one folder per job under the
/// configured schedules directory, holding the generated wrapper script and its per-run logs.
/// Keeping this behind an interface (over <see cref="IFileSystem"/>) keeps the endpoints testable
/// and guarantees Narnia only ever writes inside its own app-data folder.
/// </summary>
public interface IScheduledJobWorkspace
{
    /// <summary>The full path of a job's generated wrapper script (whether or not it exists yet).</summary>
    string ScriptPath(string jobId);

    /// <summary>
    /// The full path of a job's hidden-launcher VBScript shim (whether or not it exists yet) — the
    /// scheduled task's actual action, so the wrapper script never shows a visible console window.
    /// </summary>
    string LauncherPath(string jobId);

    /// <summary>The full path of a job's per-run log directory.</summary>
    string LogDirectory(string jobId);

    /// <summary>Writes the wrapper script for a job, creating its folder, and returns the script path.</summary>
    ValueTask<string> WriteScriptAsync(string jobId, string content, CancellationToken ct = default);

    /// <summary>Writes the hidden-launcher shim for a job, creating its folder, and returns its path.</summary>
    ValueTask<string> WriteLauncherAsync(string jobId, string content, CancellationToken ct = default);

    /// <summary>
    /// The full path of the most recent per-run log file for a job, or <c>null</c> if the job has
    /// never run (or its log directory does not exist yet).
    /// </summary>
    string? LatestLogFile(string jobId);

    /// <summary>Reads the full content of a log file (a path returned by <see cref="LatestLogFile"/>).</summary>
    ValueTask<string> ReadLogAsync(string logFilePath, CancellationToken ct = default);

    /// <summary>Removes a job's entire workspace folder. Best-effort; never throws if it is absent.</summary>
    void Delete(string jobId);
}

public sealed class ScheduledJobWorkspace(NarniaOptions options, IFileSystem fileSystem) : IScheduledJobWorkspace
{
    private string JobDir(string jobId) => fileSystem.Path.Combine(options.SchedulesDirectory, jobId);

    /// <inheritdoc />
    public string ScriptPath(string jobId) => fileSystem.Path.Combine(JobDir(jobId), "run.ps1");

    /// <inheritdoc />
    public string LauncherPath(string jobId) => fileSystem.Path.Combine(JobDir(jobId), "run.vbs");

    /// <inheritdoc />
    public string LogDirectory(string jobId) => fileSystem.Path.Combine(JobDir(jobId), "logs");

    /// <inheritdoc />
    public async ValueTask<string> WriteScriptAsync(string jobId, string content, CancellationToken ct = default)
    {
        var dir = JobDir(jobId);
        if (!fileSystem.Directory.Exists(dir))
            fileSystem.Directory.CreateDirectory(dir);

        var path = ScriptPath(jobId);
        await fileSystem.File.WriteAllTextAsync(path, content, ct);
        return path;
    }

    /// <inheritdoc />
    public async ValueTask<string> WriteLauncherAsync(string jobId, string content, CancellationToken ct = default)
    {
        var dir = JobDir(jobId);
        if (!fileSystem.Directory.Exists(dir))
            fileSystem.Directory.CreateDirectory(dir);

        var path = LauncherPath(jobId);
        await fileSystem.File.WriteAllTextAsync(path, content, ct);
        return path;
    }

    /// <inheritdoc />
    public string? LatestLogFile(string jobId)
    {
        var dir = LogDirectory(jobId);
        if (!fileSystem.Directory.Exists(dir))
            return null;

        // Log file names are "run-yyyy-MM-dd_HHmmss.log", so lexicographic order is chronological.
        return fileSystem.Directory.GetFiles(dir, "run-*.log")
            .OrderByDescending(path => path, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    /// <inheritdoc />
    public async ValueTask<string> ReadLogAsync(string logFilePath, CancellationToken ct = default) =>
        await fileSystem.File.ReadAllTextAsync(logFilePath, ct);

    /// <inheritdoc />
    public void Delete(string jobId)
    {
        var dir = JobDir(jobId);
        if (fileSystem.Directory.Exists(dir))
            fileSystem.Directory.Delete(dir, recursive: true);
    }
}
