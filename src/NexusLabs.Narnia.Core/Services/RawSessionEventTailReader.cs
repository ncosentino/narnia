using System.Buffers;
using System.IO.Abstractions;
using System.Text;
using System.Text.Json;
using NexusLabs.Narnia.Core.Configuration;
using NexusLabs.Narnia.Core.Models;

namespace NexusLabs.Narnia.Core.Services;

/// <summary>Scans raw Copilot JSONL with bounded memory and retains recent direction.</summary>
public sealed class RawSessionEventTailReader(
    NarniaOptions options,
    IFileSystem fileSystem) : IRawSessionEventTailReader
{
    private const int MaximumMessagesPerKind = 24;
    private const int MaximumLineBytes = 1_048_576;
    private const int ReadBufferBytes = 65_536;

    public async ValueTask<RawSessionEventTail> ReadAsync(
        string sessionId,
        IReadOnlyList<Turn> indexedTurns,
        CancellationToken ct = default)
    {
        if (!TryResolvePath(sessionId, out var path) || !fileSystem.File.Exists(path))
            return RawSessionEventTail.Empty;

        var directMessages = new Queue<RawSessionEvent>();
        var steeringMessages = new Queue<RawSessionEvent>();
        var truncated = false;
        await using var stream = fileSystem.File.Open(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        var remaining = stream.Length;
        var readBuffer = ArrayPool<byte>.Shared.Rent(ReadBufferBytes);
        var lineBuffer = ArrayPool<byte>.Shared.Rent(MaximumLineBytes);
        var lineLength = 0;
        var lineOverflow = false;
        try
        {
            while (remaining > 0)
            {
                var requested = (int)Math.Min(readBuffer.Length, remaining);
                var read = await stream.ReadAsync(readBuffer.AsMemory(0, requested), ct);
                if (read == 0)
                {
                    truncated = true;
                    break;
                }

                remaining -= read;
                var chunk = readBuffer.AsSpan(0, read);
                while (!chunk.IsEmpty)
                {
                    var newline = chunk.IndexOf((byte)'\n');
                    var segment = newline >= 0 ? chunk[..newline] : chunk;
                    if (!lineOverflow)
                    {
                        if (segment.Length <= MaximumLineBytes - lineLength)
                        {
                            segment.CopyTo(lineBuffer.AsSpan(lineLength));
                            lineLength += segment.Length;
                        }
                        else
                        {
                            lineOverflow = true;
                            lineLength = 0;
                            truncated = true;
                        }
                    }

                    if (newline < 0)
                        break;

                    if (!lineOverflow)
                    {
                        ProcessLine(
                            lineBuffer.AsMemory(0, TrimCarriageReturn(lineBuffer, lineLength)),
                            directMessages,
                            steeringMessages,
                            ref truncated);
                    }
                    lineLength = 0;
                    lineOverflow = false;
                    chunk = chunk[(newline + 1)..];
                }
            }

            if (!lineOverflow && lineLength > 0)
            {
                ProcessLine(
                    lineBuffer.AsMemory(0, TrimCarriageReturn(lineBuffer, lineLength)),
                    directMessages,
                    steeringMessages,
                    ref truncated);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(readBuffer);
            ArrayPool<byte>.Shared.Return(lineBuffer);
        }

        var direct = directMessages.ToArray();
        var steering = steeringMessages.ToArray();
        var latestTimestamp = direct
            .Concat(steering)
            .Where(item => item.Timestamp.HasValue)
            .Select(item => item.Timestamp!.Value)
            .DefaultIfEmpty()
            .Max();
        var hasTimestamp = direct.Concat(steering).Any(item => item.Timestamp.HasValue);
        return new RawSessionEventTail(
            direct,
            steering,
            hasTimestamp ? latestTimestamp : null,
            truncated,
            IsIndexStale(direct, indexedTurns));
    }

    private void ProcessLine(
        ReadOnlyMemory<byte> line,
        Queue<RawSessionEvent> directMessages,
        Queue<RawSessionEvent> steeringMessages,
        ref bool truncated)
    {
        if (line.IsEmpty ||
            (line.Span.IndexOf("\"user.message\""u8) < 0 &&
             line.Span.IndexOf("\"steering\""u8) < 0))
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("type", out var typeElement) ||
                typeElement.ValueKind != JsonValueKind.String)
            {
                return;
            }

            var type = typeElement.GetString()!;
            var message = FindMessage(root);
            if (string.IsNullOrWhiteSpace(message))
                return;

            var source = FindSource(root);
            var projected = new RawSessionEvent(type, FindTimestamp(root), message, source);
            var destination = projected.IsDirectUserMessage
                ? directMessages
                : steeringMessages;
            destination.Enqueue(projected);
            if (destination.Count > MaximumMessagesPerKind)
            {
                destination.Dequeue();
                truncated = true;
            }
        }
        catch (JsonException)
        {
            truncated = true;
        }
    }

    private static bool IsIndexStale(
        IReadOnlyList<RawSessionEvent> directMessages,
        IReadOnlyList<Turn> indexedTurns)
    {
        if (directMessages.Count == 0)
            return false;

        var indexedMessages = indexedTurns
            .Select(turn => Normalize(turn.UserMessage))
            .Where(message => message.Length > 0)
            .ToHashSet(StringComparer.Ordinal);
        var latestDirect = directMessages[^1];
        if (!indexedMessages.Contains(Normalize(latestDirect.Message)))
            return true;

        var latestIndexedTimestamp = indexedTurns.Count == 0
            ? (DateTimeOffset?)null
            : indexedTurns.Max(turn => turn.Timestamp);
        return latestDirect.Timestamp is not null &&
            (latestIndexedTimestamp is null || latestDirect.Timestamp > latestIndexedTimestamp);
    }

    private bool TryResolvePath(string sessionId, out string path)
    {
        var root = fileSystem.Path.GetFullPath(options.SessionStatePath)
            .TrimEnd(fileSystem.Path.DirectorySeparatorChar, fileSystem.Path.AltDirectorySeparatorChar);
        var directory = fileSystem.Path.GetFullPath(fileSystem.Path.Combine(root, sessionId));
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        path = fileSystem.Path.Combine(directory, "events.jsonl");
        return Guid.TryParse(sessionId, out _) &&
            string.Equals(fileSystem.Path.GetDirectoryName(directory), root, comparison);
    }

    private static int TrimCarriageReturn(byte[] buffer, int length) =>
        length > 0 && buffer[length - 1] == (byte)'\r' ? length - 1 : length;

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        var normalized = new StringBuilder(value.Length);
        var pendingSpace = false;
        foreach (var character in value.Trim())
        {
            if (char.IsWhiteSpace(character))
            {
                pendingSpace = normalized.Length > 0;
                continue;
            }
            if (pendingSpace)
            {
                normalized.Append(' ');
                pendingSpace = false;
            }
            normalized.Append(character);
        }
        return normalized.ToString();
    }

    private static string? FindMessage(JsonElement root)
    {
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
            return null;
        foreach (var name in new[] { "content", "message", "text", "user_message" })
        {
            if (data.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
                return value.GetString();
        }
        return null;
    }

    private static string? FindSource(JsonElement root)
    {
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object ||
            !data.TryGetProperty("source", out var source) || source.ValueKind != JsonValueKind.String)
        {
            return null;
        }
        return source.GetString();
    }

    private static DateTimeOffset? FindTimestamp(JsonElement root)
    {
        if (root.TryGetProperty("timestamp", out var timestamp) &&
            timestamp.ValueKind == JsonValueKind.String &&
            DateTimeOffset.TryParse(timestamp.GetString(), out var parsed))
        {
            return parsed;
        }
        return null;
    }
}
