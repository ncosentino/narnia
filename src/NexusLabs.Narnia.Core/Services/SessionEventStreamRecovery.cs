using System.IO.Abstractions;
using System.Security.Cryptography;
using NexusLabs.Narnia.Core.Configuration;
using NexusLabs.Narnia.Core.Models;

namespace NexusLabs.Narnia.Core.Services;

/// <summary>Archives and restores only <c>events.jsonl</c> within one validated session folder.</summary>
public sealed class SessionEventStreamRecovery(
    NarniaOptions options,
    IFileSystem fileSystem) : ISessionEventStreamRecovery
{
    /// <inheritdoc />
    public async ValueTask<SessionEventArchivePlanResult> PlanAsync(
        string sessionId,
        string migrationId,
        CancellationToken ct)
    {
        if (!TryResolveSessionDirectory(sessionId, out var sessionDirectory))
            return PlanFailure("Session identifier does not resolve beneath session-state.");
        if (!Guid.TryParse(migrationId, out _))
            return PlanFailure("Migration identifier must be a GUID.");

        var eventsPath = fileSystem.Path.Combine(sessionDirectory, "events.jsonl");
        if (!fileSystem.File.Exists(eventsPath))
            return PlanFailure("The source event stream does not exist.");

        var archivePath = fileSystem.Path.Combine(
            sessionDirectory,
            $"events.pre-recovery.{migrationId}.jsonl");
        if (fileSystem.File.Exists(archivePath))
            return PlanFailure("The migration event archive already exists.");

        try
        {
            var sha256 = await HashAsync(eventsPath, ct);
            return new SessionEventArchivePlanResult(true, archivePath, sha256, null);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return PlanFailure($"The event stream could not be planned for archival: {exception.Message}");
        }
    }

    /// <inheritdoc />
    public async ValueTask<SessionEventArchiveResult> ArchiveAsync(
        string sessionId,
        string archivePath,
        string expectedSha256,
        CancellationToken ct)
    {
        if (!TryResolveSessionDirectory(sessionId, out var sessionDirectory))
            return ArchiveFailure("Session identifier does not resolve beneath session-state.");
        if (!TryValidateArchivePath(sessionDirectory, archivePath))
            return ArchiveFailure("Archived event path does not belong to the requested session.");

        var eventsPath = fileSystem.Path.Combine(sessionDirectory, "events.jsonl");
        if (!fileSystem.File.Exists(eventsPath))
            return ArchiveFailure("The source event stream does not exist.");
        if (fileSystem.File.Exists(archivePath))
            return ArchiveFailure("The migration event archive already exists.");

        try
        {
            var sourceHash = await HashAsync(eventsPath, ct);
            if (!string.Equals(
                    sourceHash,
                    expectedSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                return ArchiveFailure("The event stream changed after its recovery plan was recorded.");
            }

            ct.ThrowIfCancellationRequested();
            fileSystem.File.Move(eventsPath, archivePath);
            var archivedHash = await HashAsync(archivePath, CancellationToken.None);
            if (!string.Equals(
                    expectedSha256,
                    archivedHash,
                    StringComparison.OrdinalIgnoreCase))
            {
                fileSystem.File.Move(archivePath, eventsPath);
                return ArchiveFailure("The archived event stream failed its integrity check.");
            }

            return new SessionEventArchiveResult(
                true,
                archivePath,
                expectedSha256,
                null);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            var rollbackError = TryRestoreArchivedOriginal(eventsPath, archivePath);
            var error = $"The event stream could not be archived: {exception.Message}";
            if (rollbackError is not null)
                error += $" Rollback also failed: {rollbackError}";
            return ArchiveFailure(error);
        }
    }

    /// <inheritdoc />
    public async ValueTask<SessionEventRestoreResult> RestoreAsync(
        string sessionId,
        string migrationId,
        string archivePath,
        string expectedSha256,
        CancellationToken ct)
    {
        if (!TryResolveSessionDirectory(sessionId, out var sessionDirectory))
            return RestoreFailure("Session identifier does not resolve beneath session-state.");
        if (!Guid.TryParse(migrationId, out _))
            return RestoreFailure("Migration identifier must be a GUID.");
        if (!TryValidateArchivePath(sessionDirectory, archivePath))
            return RestoreFailure("Archived event path does not belong to the requested session.");
        var eventsPath = fileSystem.Path.Combine(sessionDirectory, "events.jsonl");
        if (!fileSystem.File.Exists(archivePath))
        {
            if (!fileSystem.File.Exists(eventsPath))
                return RestoreFailure("Neither the archived nor active original event stream exists.");

            try
            {
                var currentHash = await HashAsync(eventsPath, ct);
                return string.Equals(
                    currentHash,
                    expectedSha256,
                    StringComparison.OrdinalIgnoreCase)
                    ? new SessionEventRestoreResult(true, null, null)
                    : RestoreFailure("The archive is missing and the active event stream does not match the original hash.");
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                return RestoreFailure($"The active event stream could not be verified: {exception.Message}");
            }
        }

        string archiveHash;
        try
        {
            archiveHash = await HashAsync(archivePath, ct);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return RestoreFailure($"The archived event stream could not be verified: {exception.Message}");
        }

        if (!string.Equals(
                archiveHash,
                expectedSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            return RestoreFailure("The archived original event stream no longer matches its recorded hash.");
        }

        ct.ThrowIfCancellationRequested();
        var failedPath = fileSystem.Path.Combine(
            sessionDirectory,
            $"events.failed-recovery.{migrationId}.jsonl");
        if (fileSystem.File.Exists(failedPath))
            return RestoreFailure("A failed-recovery event archive already exists.");

        var movedReplacement = false;
        try
        {
            if (fileSystem.File.Exists(eventsPath))
            {
                fileSystem.File.Move(eventsPath, failedPath);
                movedReplacement = true;
            }

            fileSystem.File.Move(archivePath, eventsPath);
            var restoredHash = await HashAsync(eventsPath, CancellationToken.None);
            if (!string.Equals(
                    restoredHash,
                    expectedSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException("The restored event stream failed its integrity check.");
            }

            return new SessionEventRestoreResult(
                true,
                movedReplacement ? failedPath : null,
                null);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            var rollbackError = TryRestoreReplacement(
                eventsPath,
                failedPath,
                movedReplacement);
            var error = $"The original event stream could not be restored: {exception.Message}";
            if (rollbackError is not null)
                error += $" Replacement rollback also failed: {rollbackError}";
            return RestoreFailure(error);
        }
    }

    private async ValueTask<string> HashAsync(string path, CancellationToken ct)
    {
        await using var stream = fileSystem.File.Open(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        var hash = await SHA256.HashDataAsync(stream, ct);
        return Convert.ToHexString(hash);
    }

    private string? TryRestoreReplacement(
        string eventsPath,
        string failedPath,
        bool movedReplacement)
    {
        if (!movedReplacement ||
            fileSystem.File.Exists(eventsPath) ||
            !fileSystem.File.Exists(failedPath))
        {
            return null;
        }

        try
        {
            fileSystem.File.Move(failedPath, eventsPath);
            return null;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return exception.Message;
        }
    }

    private string? TryRestoreArchivedOriginal(
        string eventsPath,
        string archivePath)
    {
        if (fileSystem.File.Exists(eventsPath) ||
            !fileSystem.File.Exists(archivePath))
        {
            return null;
        }

        try
        {
            fileSystem.File.Move(archivePath, eventsPath);
            return null;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return exception.Message;
        }
    }

    private bool TryResolveSessionDirectory(
        string sessionId,
        out string sessionDirectory)
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

    private bool TryValidateArchivePath(
        string sessionDirectory,
        string archivePath)
    {
        var fullArchivePath = fileSystem.Path.GetFullPath(archivePath);
        return string.Equals(
                fileSystem.Path.GetDirectoryName(fullArchivePath),
                sessionDirectory,
                OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal)
            && fileSystem.Path.GetFileName(fullArchivePath)
                .StartsWith("events.pre-recovery.", StringComparison.Ordinal)
            && fullArchivePath.EndsWith(".jsonl", StringComparison.Ordinal);
    }

    private static SessionEventArchiveResult ArchiveFailure(string error) =>
        new(false, null, null, error);

    private static SessionEventArchivePlanResult PlanFailure(string error) =>
        new(false, null, null, error);

    private static SessionEventRestoreResult RestoreFailure(string error) =>
        new(false, null, error);
}
