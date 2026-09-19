using System.IO.Abstractions;
using System.Text;
using System.Text.Json;
using NexusLabs.Narnia.Core.Configuration;
using NexusLabs.Narnia.Core.Models;

namespace NexusLabs.Narnia.Core.Services;

/// <summary>Reads only a bounded tail of a raw Copilot JSONL event stream.</summary>
public sealed class RawSessionEventTailReader(
    NarniaOptions options,
    IFileSystem fileSystem) : IRawSessionEventTailReader
{
    private const int TailBytes = 1_048_576;

    public async ValueTask<RawSessionEventTail> ReadAsync(
        Session session,
        CancellationToken ct = default)
    {
        if (!Guid.TryParse(session.Id, out _))
            return RawSessionEventTail.Empty;

        var root = fileSystem.Path.GetFullPath(options.SessionStatePath)
            .TrimEnd(fileSystem.Path.DirectorySeparatorChar, fileSystem.Path.AltDirectorySeparatorChar);
        var directory = fileSystem.Path.GetFullPath(fileSystem.Path.Combine(root, session.Id));
        if (!string.Equals(
                fileSystem.Path.GetDirectoryName(directory),
                root,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            return RawSessionEventTail.Empty;

        var path = fileSystem.Path.Combine(directory, "events.jsonl");
        if (!fileSystem.File.Exists(path))
            return RawSessionEventTail.Empty;

        await using var stream = fileSystem.File.Open(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        var length = stream.Length;
        var start = Math.Max(0, length - TailBytes);
        stream.Seek(start, SeekOrigin.Begin);
        var buffer = new byte[checked((int)(length - start))];
        var read = 0;
        while (read < buffer.Length)
        {
            var count = await stream.ReadAsync(buffer.AsMemory(read), ct);
            if (count == 0)
                break;
            read += count;
        }

        var text = Encoding.UTF8.GetString(buffer, 0, read);
        var lines = text.Split('\n');
        var truncated = start > 0;
        if (start > 0 && lines.Length > 0)
            lines = lines[1..];

        var events = new List<RawSessionEvent>();
        foreach (var line in lines)
        {
            ct.ThrowIfCancellationRequested();
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
                continue;
            try
            {
                using var document = JsonDocument.Parse(trimmed);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                    continue;
                var rootElement = document.RootElement;
                if (!rootElement.TryGetProperty("type", out var typeElement) ||
                    typeElement.ValueKind != JsonValueKind.String)
                    continue;
                var type = typeElement.GetString()!;
                var timestamp = FindTimestamp(rootElement);
                var message = IsUserEvent(type) ? FindMessage(rootElement) : null;
                events.Add(new RawSessionEvent(type, timestamp, message));
            }
            catch (JsonException)
            {
                truncated = true;
            }
        }

        var timestamps = events
            .Select(item => item.Timestamp)
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToArray();
        DateTimeOffset? latestTimestamp = timestamps.Length == 0 ? null : timestamps.Max();
        return new RawSessionEventTail(
            events,
            latestTimestamp,
            truncated,
            latestTimestamp is not null && latestTimestamp > session.UpdatedAt);
    }

    private static bool IsUserEvent(string type) =>
        type.Contains("user.message", StringComparison.OrdinalIgnoreCase) ||
        type.Contains("steering", StringComparison.OrdinalIgnoreCase);

    private static string? FindMessage(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;
        foreach (var property in element.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.String &&
                property.Name is "content" or "message" or "text" or "user_message")
                return property.Value.GetString();
            if (property.Value.ValueKind == JsonValueKind.Object)
            {
                var nested = FindMessage(property.Value);
                if (!string.IsNullOrWhiteSpace(nested))
                    return nested;
            }
        }
        return null;
    }

    private static DateTimeOffset? FindTimestamp(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;
        foreach (var property in element.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.String &&
                property.Name is "timestamp" or "created_at" or "createdAt" or "time" &&
                DateTimeOffset.TryParse(property.Value.GetString(), out var timestamp))
                return timestamp;
            if (property.Value.ValueKind == JsonValueKind.Object)
            {
                var nested = FindTimestamp(property.Value);
                if (nested is not null)
                    return nested;
            }
        }
        return null;
    }
}
