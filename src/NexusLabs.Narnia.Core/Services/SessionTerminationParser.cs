using System.Text.Json;
using NexusLabs.Narnia.Core.Models;

namespace NexusLabs.Narnia.Core.Services;

/// <summary>
/// Classifies how a Copilot session ended by reading its <c>events.jsonl</c> stream. Kept pure so
/// the event shapes it depends on can be pinned by tests without a file system or a live session.
/// </summary>
public static class SessionTerminationParser
{
    private const string AbortType = "abort";
    internal const string BackgroundTaskWaitTimeout = "background_task_wait_timeout";

    // Any of these appearing after an abort means the session carried on working, so the abort was
    // not what ended it. Tool completions are deliberately excluded: a cancelled tool can still
    // report back after the abort that cancelled it.
    private static readonly HashSet<string> ResumptionTypes = new(StringComparer.Ordinal)
    {
        "assistant.turn_start",
        "assistant.turn_end",
        "user.message",
    };

    /// <summary>
    /// Classifies a session's ending from its event lines.
    /// </summary>
    /// <param name="eventLines">
    /// JSON Lines from <c>events.jsonl</c>, in file order. A tail of the file is acceptable;
    /// unparseable lines (such as a partial first line) are ignored.
    /// </param>
    /// <returns>
    /// <see cref="ScheduledRunCompletion.Interrupted"/> with the recorded reason when the last thing
    /// the session did was abort or abandon pending background work,
    /// <see cref="ScheduledRunCompletion.Completed"/> when a shutdown was read without an
    /// unresolved interruption, and <see cref="ScheduledRunCompletion.Unknown"/> otherwise.
    /// </returns>
    public static SessionTermination Classify(IEnumerable<string> eventLines)
    {
        ArgumentNullException.ThrowIfNull(eventLines);

        var sawShutdown = false;
        var sawAbort = false;
        var sawBackgroundTimeout = false;
        string? abortReason = null;

        foreach (var line in eventLines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            if (!TryReadEvent(line, out var type, out var reason))
                continue;

            if (type is "session.start" or "user.message")
            {
                sawShutdown = false;
                sawBackgroundTimeout = false;
                sawAbort = false;
                abortReason = null;
            }

            if (string.Equals(reason, BackgroundTaskWaitTimeout, StringComparison.Ordinal)
                && type == "session.warning")
            {
                sawBackgroundTimeout = true;
            }
            else if (string.Equals(type, AbortType, StringComparison.Ordinal))
            {
                sawAbort = true;
                abortReason = reason ?? abortReason;
            }
            else if (ResumptionTypes.Contains(type))
            {
                sawShutdown = false;
                sawAbort = false;
                abortReason = null;
            }

            if (type == "session.shutdown")
                sawShutdown = true;
        }

        // In-flight turns and child completions can arrive after the CLI has given up waiting.
        if (sawBackgroundTimeout)
            return new SessionTermination(ScheduledRunCompletion.Interrupted, BackgroundTaskWaitTimeout);

        if (sawAbort)
            return new SessionTermination(ScheduledRunCompletion.Interrupted, abortReason);

        return sawShutdown
            ? new SessionTermination(ScheduledRunCompletion.Completed, null)
            : new SessionTermination(ScheduledRunCompletion.Unknown, null);
    }

    private static bool TryReadEvent(string line, out string type, out string? reason)
    {
        type = string.Empty;
        reason = null;

        try
        {
            using var document = JsonDocument.Parse(line);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return false;
            if (!document.RootElement.TryGetProperty("type", out var typeElement))
                return false;
            if (typeElement.ValueKind != JsonValueKind.String)
                return false;

            type = typeElement.GetString() ?? string.Empty;
            if (type.Length == 0)
                return false;

            if (document.RootElement.TryGetProperty("data", out var data)
                && data.ValueKind == JsonValueKind.Object)
            {
                var reasonProperty = type == "session.warning" ? "warningType" : "reason";
                if (data.TryGetProperty(reasonProperty, out var reasonElement)
                    && reasonElement.ValueKind == JsonValueKind.String)
                {
                    reason = reasonElement.GetString();
                }
            }

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

/// <summary>How a session's event stream ended.</summary>
/// <param name="Completion">The classification.</param>
/// <param name="AbortReason">The abort reason or terminal warning type, when one was recorded.</param>
public readonly record struct SessionTermination(
    ScheduledRunCompletion Completion,
    string? AbortReason);
