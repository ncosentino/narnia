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
    /// the session did was abort, <see cref="ScheduledRunCompletion.Completed"/> when recognizable
    /// events were read and none of them ended it, and
    /// <see cref="ScheduledRunCompletion.Unknown"/> when nothing could be read.
    /// </returns>
    public static SessionTermination Classify(IEnumerable<string> eventLines)
    {
        ArgumentNullException.ThrowIfNull(eventLines);

        var sawEvent = false;
        var sawAbort = false;
        string? abortReason = null;

        foreach (var line in eventLines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            if (!TryReadEvent(line, out var type, out var reason))
                continue;

            sawEvent = true;

            if (string.Equals(type, AbortType, StringComparison.Ordinal))
            {
                sawAbort = true;
                abortReason = reason ?? abortReason;
            }
            else if (ResumptionTypes.Contains(type))
            {
                sawAbort = false;
                abortReason = null;
            }
        }

        if (sawAbort)
            return new SessionTermination(ScheduledRunCompletion.Interrupted, abortReason);

        return sawEvent
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
                && data.ValueKind == JsonValueKind.Object
                && data.TryGetProperty("reason", out var reasonElement)
                && reasonElement.ValueKind == JsonValueKind.String)
            {
                reason = reasonElement.GetString();
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
/// <param name="AbortReason">The reason recorded on the abort that ended the session, when there was one.</param>
public readonly record struct SessionTermination(
    ScheduledRunCompletion Completion,
    string? AbortReason);
