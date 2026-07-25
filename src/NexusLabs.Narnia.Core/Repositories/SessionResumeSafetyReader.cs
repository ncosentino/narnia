using System.IO.Abstractions;
using System.Text.Json;
using NexusLabs.Narnia.Core.Configuration;
using NexusLabs.Narnia.Core.Models;

namespace NexusLabs.Narnia.Core.Repositories;

/// <summary>Reads the first Copilot event and nested-agent metadata through read-only access.</summary>
public sealed class SessionResumeSafetyReader(
    NarniaOptions options,
    IFileSystem fileSystem,
    IWorkspaceReader workspaceReader) : ISessionResumeSafetyReader
{
    /// <inheritdoc />
    public SessionResumeAssessment Inspect(string sessionId)
    {
        if (!TryResolveSessionDirectory(sessionId, out var sessionDirectory))
        {
            return new SessionResumeAssessment(
                sessionId,
                SessionResumeSafety.Incompatible,
                "Session identifier does not resolve beneath the configured session-state directory.",
                null,
                false);
        }

        var workspace = ReadWorkspace(sessionId);
        var eventsPath = fileSystem.Path.Combine(sessionDirectory, "events.jsonl");
        if (!fileSystem.File.Exists(eventsPath))
        {
            return new SessionResumeAssessment(
                sessionId,
                SessionResumeSafety.Unknown,
                "No local event stream is available for a deterministic resume check.",
                null,
                workspace.IsNestedAgent);
        }

        try
        {
            using var stream = fileSystem.File.Open(
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
                    workspace.IsNestedAgent);
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
                    workspace.IsNestedAgent);
            }

            var firstEventType = type.GetString();
            if (string.Equals(firstEventType, "session.start", StringComparison.Ordinal))
            {
                return new SessionResumeAssessment(
                    sessionId,
                    SessionResumeSafety.Resumable,
                    null,
                    firstEventType,
                    workspace.IsNestedAgent);
            }

            var reason = workspace.IsNestedAgent
                ? $"This is a nested Copilot agent session whose history starts with '{firstEventType ?? "(missing type)"}' instead of the required 'session.start' event."
                : $"The event stream starts with '{firstEventType ?? "(missing type)"}' instead of the required 'session.start' event.";
            return new SessionResumeAssessment(
                sessionId,
                SessionResumeSafety.Incompatible,
                reason,
                firstEventType,
                workspace.IsNestedAgent);
        }
        catch (JsonException exception)
        {
            return new SessionResumeAssessment(
                sessionId,
                SessionResumeSafety.Incompatible,
                $"The first persisted event is invalid JSON: {exception.Message}",
                null,
                workspace.IsNestedAgent);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return new SessionResumeAssessment(
                sessionId,
                SessionResumeSafety.Unknown,
                $"The local event stream could not be inspected: {exception.Message}",
                null,
                workspace.IsNestedAgent);
        }
    }

    private WorkspaceInfo ReadWorkspace(string sessionId)
    {
        try
        {
            return workspaceReader.ReadMetadata(sessionId);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return new WorkspaceInfo(sessionId, null, []);
        }
    }

    private bool TryResolveSessionDirectory(string sessionId, out string sessionDirectory)
    {
        var root = fileSystem.Path.GetFullPath(options.SessionStatePath)
            .TrimEnd(
                fileSystem.Path.DirectorySeparatorChar,
                fileSystem.Path.AltDirectorySeparatorChar);
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
