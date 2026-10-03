using System.Text.RegularExpressions;

namespace NexusLabs.Narnia.Core.Services;

/// <summary>
/// Pure parsing of a scheduled job's run log. Kept free of I/O so the log formats Narnia's own
/// wrapper produces can be pinned by tests without touching a file system.
/// </summary>
public static partial class ScheduledRunLog
{
    /// <summary>Finds the CLI's terminal warning that it abandoned pending background work.</summary>
    public static string? FindInterruptionReason(string? logText) =>
        !string.IsNullOrEmpty(logText) && BackgroundTimeoutPattern().IsMatch(logText)
            ? SessionTerminationParser.BackgroundTaskWaitTimeout
            : null;

    /// <summary>
    /// Finds the Copilot session a run log belongs to, from the <c>--resume=</c> footer the CLI
    /// prints when it exits.
    /// </summary>
    /// <param name="logText">Log text, or a tail of it.</param>
    /// <returns>The session identifier, or <c>null</c> when the log names none.</returns>
    /// <remarks>
    /// The last match wins: a job's prompt is echoed into the top of its own log, so an earlier
    /// occurrence may belong to the prompt rather than to the run that just finished.
    /// </remarks>
    public static string? FindSessionId(string? logText)
    {
        if (string.IsNullOrEmpty(logText))
            return null;

        string? found = null;
        foreach (Match match in ResumePattern().Matches(logText))
            found = match.Groups[1].Value;

        return found;
    }

    [GeneratedRegex(
        @"--resume[=\s]+([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})",
        RegexOptions.CultureInvariant)]
    private static partial Regex ResumePattern();

    [GeneratedRegex(
        @"^[ \t]*![ \t]+Timed out after [0-9]+s waiting for background tasks to finish; giving up on the wait with work still[ \t]*(?:\r?\n[ \t]*)?pending\.",
        RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex BackgroundTimeoutPattern();
}
