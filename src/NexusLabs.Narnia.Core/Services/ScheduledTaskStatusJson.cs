using System.Text.Json;
using NexusLabs.Narnia.Core.Models;

namespace NexusLabs.Narnia.Core.Services;

/// <summary>
/// Parses the compact JSON lines emitted when reading the OS scheduler into
/// <see cref="ScheduledTaskStatus"/> values. Kept separate from the platform-specific provider so
/// the parsing — sentinel handling, state mapping, type coercion — is unit-testable without a
/// scheduler. Each input line is one JSON object with <c>folder</c>, <c>name</c>, <c>state</c>,
/// <c>lastRunTime</c>, <c>lastResult</c>, <c>nextRunTime</c>, and <c>action</c> fields.
/// </summary>
public static class ScheduledTaskStatusJson
{
    /// <summary>Parses a single JSON line, returning <c>null</c> if it is blank or malformed.</summary>
    public static ScheduledTaskStatus? ParseLine(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;

        var trimmed = line.Trim();
        if (trimmed[0] != '{')
            return null;

        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            var root = doc.RootElement;

            return new ScheduledTaskStatus(
                TaskFolder: GetString(root, "folder") ?? "",
                TaskName: GetString(root, "name") ?? "",
                State: ParseState(GetString(root, "state")),
                LastRunTime: GetDate(root, "lastRunTime"),
                LastResult: GetInt(root, "lastResult"),
                NextRunTime: GetDate(root, "nextRunTime"),
                ActionSummary: GetString(root, "action"));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Parses every JSON line in <paramref name="output"/>, skipping blank/malformed lines.</summary>
    public static IReadOnlyList<ScheduledTaskStatus> ParseLines(string? output)
    {
        if (string.IsNullOrEmpty(output))
            return [];

        var results = new List<ScheduledTaskStatus>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var status = ParseLine(line);
            if (status is not null)
                results.Add(status);
        }

        return results;
    }

    private static string? GetString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;

    private static int? GetInt(JsonElement root, string name) =>
        root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var v)
            ? v
            : null;

    private static DateTimeOffset? GetDate(JsonElement root, string name) =>
        root.TryGetProperty(name, out var el)
        && el.ValueKind == JsonValueKind.String
        && DateTimeOffset.TryParse(el.GetString(), out var dt)
            ? dt
            : null;

    private static ScheduledTaskState ParseState(string? value) =>
        Enum.TryParse<ScheduledTaskState>(value, ignoreCase: true, out var state)
            ? state
            : ScheduledTaskState.Unknown;
}
