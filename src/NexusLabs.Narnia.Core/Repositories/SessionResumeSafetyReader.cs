using System.Buffers;
using System.Collections.Concurrent;
using System.IO.Abstractions;
using System.Text;
using System.Text.Json;
using NexusLabs.Narnia.Core.Configuration;
using NexusLabs.Narnia.Core.Models;

namespace NexusLabs.Narnia.Core.Repositories;

/// <summary>
/// Reads the first Copilot event, event-stream size, nested-agent metadata, and installed runtime
/// capabilities through read-only access so Narnia can select a defensible resume policy.
/// </summary>
public sealed class SessionResumeSafetyReader : ISessionResumeSafetyReader
{
    // Copilot 1.0.75 reads events.jsonl as one UTF-8 string. Its V8 runtime rejects strings beyond
    // buffer.constants.MAX_STRING_LENGTH (0x1fffffe8), then direct --resume falls back to a blank
    // session without surfacing the loader error.
    private const long MaximumSafeEventStreamCharacters = 0x1fffffe8L;
    private readonly NarniaOptions _options;
    private readonly IFileSystem _fileSystem;
    private readonly IWorkspaceReader _workspaceReader;
    private readonly ICopilotRuntimeCapabilityReader _capabilityReader;
    private readonly long _maximumSafeEventStreamCharacters;
    private readonly ConcurrentDictionary<string, CharacterLimitCacheEntry> _characterLimitCache;

    /// <summary>Initializes the resume-safety reader and discovers installed Copilot capabilities.</summary>
    /// <param name="options">Narnia paths.</param>
    /// <param name="fileSystem">Filesystem abstraction used for read-only inspection.</param>
    /// <param name="workspaceReader">Workspace metadata reader.</param>
    public SessionResumeSafetyReader(
        NarniaOptions options,
        IFileSystem fileSystem,
        IWorkspaceReader workspaceReader)
        : this(
            options,
            fileSystem,
            workspaceReader,
            new CopilotRuntimeCapabilityReader(options, fileSystem),
            MaximumSafeEventStreamCharacters)
    {
    }

    /// <summary>Initializes the resume-safety reader with explicit capability inspection.</summary>
    /// <param name="options">Narnia paths.</param>
    /// <param name="fileSystem">Filesystem abstraction used for read-only inspection.</param>
    /// <param name="workspaceReader">Workspace metadata reader.</param>
    /// <param name="capabilityReader">Installed Copilot capability reader.</param>
    public SessionResumeSafetyReader(
        NarniaOptions options,
        IFileSystem fileSystem,
        IWorkspaceReader workspaceReader,
        ICopilotRuntimeCapabilityReader capabilityReader)
        : this(
            options,
            fileSystem,
            workspaceReader,
            capabilityReader,
            MaximumSafeEventStreamCharacters)
    {
    }

    internal SessionResumeSafetyReader(
        NarniaOptions options,
        IFileSystem fileSystem,
        IWorkspaceReader workspaceReader,
        long maximumSafeEventStreamCharacters)
        : this(
            options,
            fileSystem,
            workspaceReader,
            new CopilotRuntimeCapabilityReader(options, fileSystem),
            maximumSafeEventStreamCharacters)
    {
    }

    internal SessionResumeSafetyReader(
        NarniaOptions options,
        IFileSystem fileSystem,
        IWorkspaceReader workspaceReader,
        ICopilotRuntimeCapabilityReader capabilityReader,
        long maximumSafeEventStreamCharacters)
    {
        _options = options;
        _fileSystem = fileSystem;
        _workspaceReader = workspaceReader;
        _capabilityReader = capabilityReader;
        _maximumSafeEventStreamCharacters = maximumSafeEventStreamCharacters;
        _characterLimitCache = new ConcurrentDictionary<string, CharacterLimitCacheEntry>(
            OperatingSystem.IsWindows()
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal);
    }

    /// <inheritdoc />
    public SessionResumeAssessment Inspect(string sessionId)
    {
        var capability = _capabilityReader.Read();
        var policy = capability.Support == CopilotLargeSessionResumeSupport.Supported
            ? "native-large-session-resume"
            : "legacy-character-ceiling";

        if (!TryResolveSessionDirectory(sessionId, out var sessionDirectory))
        {
            return new SessionResumeAssessment(
                sessionId,
                SessionResumeSafety.Incompatible,
                "Session identifier does not resolve beneath the configured session-state directory.",
                null,
                false,
                policy,
                capability.Version);
        }

        var workspace = ReadWorkspace(sessionId);
        var eventsPath = _fileSystem.Path.Combine(sessionDirectory, "events.jsonl");
        if (!_fileSystem.File.Exists(eventsPath))
        {
            return new SessionResumeAssessment(
                sessionId,
                SessionResumeSafety.Unknown,
                "No local event stream is available for a deterministic resume check.",
                null,
                workspace.IsNestedAgent,
                policy,
                capability.Version);
        }

        try
        {
            var eventInfo = _fileSystem.FileInfo.New(eventsPath);
            var eventStreamBytes = eventInfo.Length;
            var eventStreamLastWriteTimeUtc = eventInfo.LastWriteTimeUtc;
            using var stream = _fileSystem.File.Open(
                eventsPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            var firstLine = reader.ReadLine();
            if (string.IsNullOrWhiteSpace(firstLine))
            {
                return new SessionResumeAssessment(
                    sessionId,
                    SessionResumeSafety.Incompatible,
                    "The local event stream is empty.",
                    null,
                    workspace.IsNestedAgent,
                    policy,
                    capability.Version);
            }

            using var document = JsonDocument.Parse(firstLine);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("type", out var type) ||
                type.ValueKind != JsonValueKind.String)
            {
                return new SessionResumeAssessment(
                    sessionId,
                    SessionResumeSafety.Incompatible,
                    "The first persisted event does not contain a valid string event type.",
                    null,
                    workspace.IsNestedAgent,
                    policy,
                    capability.Version);
            }

            var firstEventType = type.GetString();
            if (string.Equals(firstEventType, "session.start", StringComparison.Ordinal))
            {
                if (capability.Support != CopilotLargeSessionResumeSupport.Supported &&
                    eventStreamBytes > _maximumSafeEventStreamCharacters &&
                    ExceedsCharacterLimit(
                        eventsPath,
                        eventStreamBytes,
                        eventStreamLastWriteTimeUtc))
                {
                    return new SessionResumeAssessment(
                        sessionId,
                        SessionResumeSafety.Incompatible,
                        $"The local event stream is {eventStreamBytes:N0} bytes and decodes beyond the conservative {_maximumSafeEventStreamCharacters:N0}-character legacy loader ceiling. The installed Copilot runtime does not advertise reliable very-large-session resume.",
                        firstEventType,
                        workspace.IsNestedAgent,
                        policy,
                        capability.Version);
                }

                return new SessionResumeAssessment(
                    sessionId,
                    SessionResumeSafety.Resumable,
                    null,
                    firstEventType,
                    workspace.IsNestedAgent,
                    policy,
                    capability.Version);
            }

            var reason = workspace.IsNestedAgent
                ? $"This is a nested Copilot agent session whose history starts with '{firstEventType ?? "(missing type)"}' instead of the required 'session.start' event."
                : $"The event stream starts with '{firstEventType ?? "(missing type)"}' instead of the required 'session.start' event.";
            return new SessionResumeAssessment(
                sessionId,
                SessionResumeSafety.Incompatible,
                reason,
                firstEventType,
                workspace.IsNestedAgent,
                policy,
                capability.Version);
        }
        catch (JsonException exception)
        {
            return new SessionResumeAssessment(
                sessionId,
                SessionResumeSafety.Incompatible,
                $"The first persisted event is invalid JSON: {exception.Message}",
                null,
                workspace.IsNestedAgent,
                policy,
                capability.Version);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return new SessionResumeAssessment(
                sessionId,
                SessionResumeSafety.Unknown,
                $"The local event stream could not be inspected: {exception.Message}",
                null,
                workspace.IsNestedAgent,
                policy,
                capability.Version);
        }
    }

    private WorkspaceInfo ReadWorkspace(string sessionId)
    {
        try
        {
            return _workspaceReader.ReadMetadata(sessionId);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return new WorkspaceInfo(sessionId, null, []);
        }
    }

    private bool TryResolveSessionDirectory(string sessionId, out string sessionDirectory)
    {
        var root = _fileSystem.Path.GetFullPath(_options.SessionStatePath)
            .TrimEnd(
                _fileSystem.Path.DirectorySeparatorChar,
                _fileSystem.Path.AltDirectorySeparatorChar);
        sessionDirectory = _fileSystem.Path.GetFullPath(
            _fileSystem.Path.Combine(root, sessionId));
        return Guid.TryParse(sessionId, out _)
            && string.Equals(
                _fileSystem.Path.GetDirectoryName(sessionDirectory),
                root,
                OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal);
    }

    private bool ExceedsCharacterLimit(
        string eventsPath,
        long expectedBytes,
        DateTime expectedLastWriteTimeUtc)
    {
        if (_characterLimitCache.TryGetValue(eventsPath, out var cached) &&
            cached.Bytes == expectedBytes &&
            cached.LastWriteTimeUtc == expectedLastWriteTimeUtc)
        {
            return cached.ExceedsLimit;
        }

        var characters = 0L;
        var buffer = ArrayPool<char>.Shared.Rent(1_048_576);
        try
        {
            using var stream = _fileSystem.File.Open(
                eventsPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(
                stream,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: false,
                bufferSize: 1_048_576);
            int read;
            while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
            {
                characters += read;
                if (characters > _maximumSafeEventStreamCharacters)
                    break;
            }
        }
        finally
        {
            ArrayPool<char>.Shared.Return(buffer);
        }

        var currentInfo = _fileSystem.FileInfo.New(eventsPath);
        if (currentInfo.Length != expectedBytes ||
            currentInfo.LastWriteTimeUtc != expectedLastWriteTimeUtc)
        {
            throw new IOException(
                "The event stream changed while its decoded length was being inspected.");
        }

        var exceedsLimit = characters > _maximumSafeEventStreamCharacters;
        _characterLimitCache[eventsPath] = new CharacterLimitCacheEntry(
            expectedBytes,
            expectedLastWriteTimeUtc,
            exceedsLimit);
        return exceedsLimit;
    }

    private sealed record CharacterLimitCacheEntry(
        long Bytes,
        DateTime LastWriteTimeUtc,
        bool ExceedsLimit);
}
