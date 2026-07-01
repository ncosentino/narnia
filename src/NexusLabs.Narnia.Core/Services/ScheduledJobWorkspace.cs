using System.IO.Abstractions;
using NexusLabs.Narnia.Core.Configuration;

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

    /// <summary>The full path of a job's per-run log directory.</summary>
    string LogDirectory(string jobId);

    /// <summary>Writes the wrapper script for a job, creating its folder, and returns the script path.</summary>
    ValueTask<string> WriteScriptAsync(string jobId, string content, CancellationToken ct = default);

    /// <summary>Removes a job's entire workspace folder. Best-effort; never throws if it is absent.</summary>
    void Delete(string jobId);
}

public sealed class ScheduledJobWorkspace(NarniaOptions options, IFileSystem fileSystem) : IScheduledJobWorkspace
{
    private string JobDir(string jobId) => fileSystem.Path.Combine(options.SchedulesDirectory, jobId);

    /// <inheritdoc />
    public string ScriptPath(string jobId) => fileSystem.Path.Combine(JobDir(jobId), "run.ps1");

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
    public void Delete(string jobId)
    {
        var dir = JobDir(jobId);
        if (fileSystem.Directory.Exists(dir))
            fileSystem.Directory.Delete(dir, recursive: true);
    }
}
