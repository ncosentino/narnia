using System.IO.Abstractions;
using NexusLabs.Narnia.Core.Configuration;
using NexusLabs.Narnia.Core.Models;

namespace NexusLabs.Narnia.Core.Services;

/// <summary>
/// Default <see cref="IScheduledRunOutcomeReader"/>. Joins a job's newest run log to the Copilot
/// session it started, then classifies that session's ending from its event stream.
/// </summary>
/// <remarks>
/// Both reads are bounded tails. A job's log grows with every run and a session's event stream can
/// reach hundreds of megabytes, and this runs once per job every time the schedule list is
/// rendered, so neither file is ever read whole.
/// </remarks>
public sealed class ScheduledRunOutcomeReader(
    NarniaOptions options,
    IScheduledJobWorkspace workspace,
    IFileSystem fileSystem) : IScheduledRunOutcomeReader
{
    // The CLI prints its "--resume=" footer in the last few lines of a run log.
    private const int LogTailBytes = 16 * 1024;

    // A single event can be large (skill bodies, encoded assets), so the window has to be wide
    // enough to still contain whole lines around the end of the stream.
    private const int EventTailBytes = 512 * 1024;

    /// <inheritdoc />
    public async ValueTask<ScheduledRunOutcome> ReadLatestAsync(
        string jobId,
        CancellationToken ct = default)
    {
        string? logPath;
        try
        {
            logPath = workspace.LatestLogFile(jobId);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ScheduledRunOutcome.Indeterminate;
        }

        if (logPath is null)
            return ScheduledRunOutcome.Indeterminate;

        var logTail = await ReadTailAsync(logPath, LogTailBytes, ct);
        var sessionId = ScheduledRunLog.FindSessionId(logTail?.Text);
        if (sessionId is null)
            return ScheduledRunOutcome.Indeterminate;

        if (!TryResolveSessionDirectory(sessionId, out var sessionDirectory))
            return ScheduledRunOutcome.Indeterminate;

        var eventsPath = fileSystem.Path.Combine(sessionDirectory, "events.jsonl");
        var events = await ReadTailAsync(eventsPath, EventTailBytes, ct);
        if (events is null)
            return new ScheduledRunOutcome(ScheduledRunCompletion.Unknown, sessionId, null);

        var termination = SessionTerminationParser.Classify(WholeLines(events.Value));
        return new ScheduledRunOutcome(termination.Completion, sessionId, termination.AbortReason);
    }

    // A truncated tail can start mid-line (and mid-UTF-8-sequence), so its first line is discarded.
    // A file read in full has no such fragment and must keep every line.
    private static IEnumerable<string> WholeLines(FileTail tail)
    {
        var lines = tail.Text.Split('\n');
        var first = tail.Truncated ? 1 : 0;
        for (var i = first; i < lines.Length; i++)
            yield return lines[i].TrimEnd('\r');
    }

    private async ValueTask<FileTail?> ReadTailAsync(string path, int maxBytes, CancellationToken ct)
    {
        try
        {
            if (!fileSystem.File.Exists(path))
                return null;

            // FileShare.ReadWrite so a run that is still appending does not make this throw.
            await using var stream = fileSystem.FileStream.New(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite);

            var truncated = stream.Length > maxBytes;
            if (truncated)
                stream.Seek(-maxBytes, SeekOrigin.End);

            using var reader = new StreamReader(stream);
            return new FileTail(await reader.ReadToEndAsync(ct), truncated);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return null;
        }
    }

    private readonly record struct FileTail(string Text, bool Truncated);

    private bool TryResolveSessionDirectory(string sessionId, out string sessionDirectory)
    {
        var root = Path.TrimEndingDirectorySeparator(
            fileSystem.Path.GetFullPath(options.SessionStatePath));
        sessionDirectory = fileSystem.Path.GetFullPath(
            fileSystem.Path.Combine(root, sessionId));
        return Guid.TryParse(sessionId, out _)
            && string.Equals(
                fileSystem.Path.GetDirectoryName(sessionDirectory),
                root,
                OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal);
    }
}
