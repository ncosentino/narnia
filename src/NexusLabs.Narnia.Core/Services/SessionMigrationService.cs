using Microsoft.Data.Sqlite;
using System.IO.Abstractions;
using NexusLabs.Narnia.Core.Configuration;
using NexusLabs.Narnia.Core.Models;
using NexusLabs.Narnia.Core.Repositories;

namespace NexusLabs.Narnia.Core.Services;

/// <summary>Orchestrates supported successor creation and Narnia reference migration.</summary>
public sealed class SessionMigrationService(
    ISessionRepository sessionRepository,
    ISessionResumeSafetyReader resumeSafetyReader,
    ISessionTaskStateReader taskStateReader,
    ISessionMigrationRepository migrationRepository,
    ISessionRecoveryPacketBuilder packetBuilder,
    ISessionEventStreamRecovery eventStreamRecovery,
    ICopilotSessionManager copilotSessionManager,
    ICopilotSessionActivityReader activityReader,
    ISessionOperationCoordinator operationCoordinator,
    NarniaOptions options,
    IFileSystem fileSystem,
    TimeProvider timeProvider) : ISessionMigrationService
{
    private const int MaximumPacketReadCharacters = 1_100_000;
    private static readonly TimeSpan PreparingStaleAfter = TimeSpan.FromMinutes(10);
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <inheritdoc />
    public async ValueTask<SessionMigrationPreview> PreviewAsync(
        string sourceSessionId,
        CancellationToken ct)
    {
        var assessment = resumeSafetyReader.Inspect(sourceSessionId);
        var session = await sessionRepository.GetByIdAsync(sourceSessionId, ct);
        var existing = await migrationRepository.GetLatestBySourceAsync(sourceSessionId, ct);
        if (session is null)
        {
            return new SessionMigrationPreview(
                sourceSessionId,
                null,
                assessment,
                false,
                0,
                0,
                0,
                EmptyReferences(),
                existing,
                "The source session is not available in the Copilot session index.");
        }

        var active = activityReader.GetActiveSessionIds().Contains(sourceSessionId);
        var references = await migrationRepository.GetReferenceSummaryAsync(sourceSessionId, ct);
        var taskState = taskStateReader.Read(sourceSessionId);
        var now = timeProvider.GetUtcNow();
        string? blockingReason = null;
        if (active)
            blockingReason = "The source session is currently owned by a live Copilot process.";
        else if (existing is { Status: SessionMigrationStatus.Preparing } &&
                 !IsStale(existing, now))
            blockingReason = "A migration for this source session is already being prepared.";
        else if (existing?.Status != SessionMigrationStatus.SessionCreated &&
                 assessment.Safety == SessionResumeSafety.Resumable)
            blockingReason =
                "This session passes Narnia's resume-safety check. Migration is limited to known incompatible histories.";
        else if (existing?.Status != SessionMigrationStatus.SessionCreated &&
                 assessment.Safety == SessionResumeSafety.Unknown)
            blockingReason =
                "Narnia cannot confirm that this session has incompatible local history.";

        return new SessionMigrationPreview(
            sourceSessionId,
            session.Summary,
            assessment,
            active,
            session.TurnCount,
            session.CheckpointCount,
            taskState.Todos.Count,
            references,
            existing,
            blockingReason);
    }

    /// <inheritdoc />
    public async ValueTask<SessionMigrationResult> MigrateAsync(
        string sourceSessionId,
        CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var knownMigration = await migrationRepository.GetLatestBySourceAsync(
                sourceSessionId,
                ct);
            IReadOnlyCollection<string> operationSessionIds = knownMigration is null
                ? [sourceSessionId]
                : [sourceSessionId, knownMigration.ReplacementSessionId];
            await using var operation = await operationCoordinator.AcquireAsync(
                operationSessionIds,
                ct);
            var preview = await PreviewAsync(sourceSessionId, ct);
            var resetExistingMigration = false;
            if (preview.ExistingMigration is
                {
                    Status: SessionMigrationStatus.Completed,
                    IsInPlace: false,
                } completed)
            {
                var resetError = await ResetIncompleteMigrationAsync(
                    completed,
                    "Successor migration was reset before same-folder recovery.",
                    ct);
                if (resetError is not null)
                    return new SessionMigrationResult(false, completed, resetError);
                resetExistingMigration = true;
            }
            if (preview.ExistingMigration is { Status: SessionMigrationStatus.SessionCreated } createdMigration)
            {
                if (createdMigration.IsInPlace)
                    return await CompleteCreatedSessionAsync(createdMigration, ct);

                var resetError = await ResetIncompleteMigrationAsync(
                    createdMigration,
                    "Legacy successor migration was reset before same-folder recovery.",
                    ct);
                if (resetError is not null)
                    return new SessionMigrationResult(false, createdMigration, resetError);
                resetExistingMigration = true;
            }
            if (preview.ExistingMigration is { Status: SessionMigrationStatus.CleanupRequired } incomplete)
            {
                var resetError = await ResetIncompleteMigrationAsync(
                    incomplete,
                    "Incomplete successor was removed before retry.",
                    ct);
                if (resetError is not null)
                    return new SessionMigrationResult(false, incomplete, resetError);
                resetExistingMigration = true;
            }
            if (preview.ExistingMigration is { Status: SessionMigrationStatus.Preparing } stale &&
                IsStale(stale, timeProvider.GetUtcNow()))
            {
                var resetError = await ResetIncompleteMigrationAsync(
                    stale,
                    "Stale preparing migration was reset before retry.",
                    ct);
                if (resetError is not null)
                    return new SessionMigrationResult(false, stale, resetError);
                resetExistingMigration = true;
            }

            if (resetExistingMigration)
                preview = await PreviewAsync(sourceSessionId, ct);
            if (!preview.CanMigrate)
                return new SessionMigrationResult(false, preview.ExistingMigration, preview.BlockingReason);

            var replacementSessionId = sourceSessionId;
            var source = await sessionRepository.GetByIdAsync(sourceSessionId, ct);
            if (source is null)
            {
                return new SessionMigrationResult(
                    false,
                    preview.ExistingMigration,
                    "The source session disappeared before recovery could begin.");
            }

            var reusableMigration = preview.ExistingMigration is
                {
                    Status: SessionMigrationStatus.Failed,
                    IsInPlace: true,
                }
                ? preview.ExistingMigration
                : null;
            var migrationId = reusableMigration?.Id ?? Guid.NewGuid().ToString();
            var packet = await packetBuilder.BuildAsync(
                sourceSessionId,
                replacementSessionId,
                migrationId,
                ct);
            if (!packet.Succeeded ||
                string.IsNullOrWhiteSpace(packet.PacketPath) ||
                string.IsNullOrWhiteSpace(packet.BootstrapPrompt))
            {
                return new SessionMigrationResult(
                    false,
                    null,
                    packet.Error ?? "Narnia could not build recovery context.");
            }

            var now = timeProvider.GetUtcNow();
            var archivePlan = await eventStreamRecovery.PlanAsync(
                sourceSessionId,
                migrationId,
                ct);
            if (!archivePlan.Planned ||
                string.IsNullOrWhiteSpace(archivePlan.ArchivePath) ||
                string.IsNullOrWhiteSpace(archivePlan.Sha256))
            {
                return new SessionMigrationResult(
                    false,
                    reusableMigration,
                    archivePlan.Error ?? "Narnia could not plan the event-stream archive.");
            }

            var migration = new SessionMigration(
                migrationId,
                sourceSessionId,
                replacementSessionId,
                SessionMigrationStatus.Preparing,
                packet.PacketPath,
                packet.PacketBytes,
                packet.PacketTruncated,
                null,
                now,
                now,
                null);
            migration = migration with
            {
                ArchivedEventsPath = archivePlan.ArchivePath,
                ArchivedEventsSha256 = archivePlan.Sha256,
                BaselineTurnCount = source.TurnCount,
                BaselineUpdatedAt = source.UpdatedAt,
            };
            try
            {
                if (reusableMigration is null)
                {
                    await migrationRepository.AddAsync(migration, ct);
                }
                else if (!await migrationRepository.RestartAsync(migration, ct))
                {
                    return new SessionMigrationResult(
                        false,
                        reusableMigration,
                        "Narnia could not restart the failed recovery record.");
                }
            }
            catch (SqliteException exception)
            {
                return new SessionMigrationResult(
                    false,
                    null,
                    $"Narnia could not record the migration before session creation: {exception.Message}");
            }

            var activeNow = activityReader.GetActiveSessionIds().Contains(sourceSessionId);
            var safetyNow = resumeSafetyReader.Inspect(sourceSessionId);
            if (activeNow || safetyNow.Safety != SessionResumeSafety.Incompatible)
            {
                var error = activeNow
                    ? "The session became active before its event stream could be archived."
                    : "The session event stream changed and is no longer a confirmed incompatible recovery candidate.";
                await TryMarkFailedAsync(migration.Id, error, CancellationToken.None);
                return new SessionMigrationResult(false, migration, error);
            }

            var archive = await eventStreamRecovery.ArchiveAsync(
                sourceSessionId,
                archivePlan.ArchivePath,
                archivePlan.Sha256,
                ct);
            if (!archive.Archived ||
                string.IsNullOrWhiteSpace(archive.ArchivePath) ||
                string.IsNullOrWhiteSpace(archive.Sha256))
            {
                var error = archive.Error ?? "Narnia could not archive the broken event stream.";
                await TryMarkFailedAsync(migration.Id, error, CancellationToken.None);
                return new SessionMigrationResult(
                    false,
                    migration with
                    {
                        Status = SessionMigrationStatus.Failed,
                        Error = error,
                        UpdatedAt = timeProvider.GetUtcNow(),
                    },
                    error);
            }
            CopilotRecoverySessionResult creation;
            try
            {
                creation = await copilotSessionManager.CreateRecoverySessionAsync(
                    new CopilotRecoverySessionRequest(
                        replacementSessionId,
                        source?.Cwd,
                        packet.BootstrapPrompt),
                    ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                await ResetCancelledMigrationAsync(migration);
                throw;
            }
            if (!creation.Created)
            {
                var error = creation.Error ?? "Copilot did not create the recovered successor.";
                return await RecordFailedCreationAsync(
                    migration,
                    error,
                    CancellationToken.None);
            }

            try
            {
                var createdAt = timeProvider.GetUtcNow();
                await migrationRepository.MarkSessionCreatedAsync(
                    migration.Id,
                    createdAt,
                    CancellationToken.None);
                var created = migration with
                {
                    Status = SessionMigrationStatus.SessionCreated,
                    UpdatedAt = createdAt,
                };
                return await CompleteCreatedSessionAsync(created, CancellationToken.None);
            }
            catch (SqliteException exception)
            {
                var error =
                    $"Copilot created successor {replacementSessionId}, but Narnia could not finalize its references: {exception.Message}";
                return await RecordFailedCreationAsync(
                    migration,
                    error,
                    CancellationToken.None);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask<SessionMigration?> GetRelatedAsync(
        string sessionId,
        CancellationToken ct)
    {
        var source = await migrationRepository.GetLatestBySourceAsync(sessionId, ct);
        return source ?? await migrationRepository.GetByReplacementAsync(sessionId, ct);
    }

    /// <inheritdoc />
    public async ValueTask<SessionRecoveryPacketChunk?> ReadPacketAsync(
        string sessionId,
        int offset,
        int maxCharacters,
        CancellationToken ct)
    {
        var migration = await GetRelatedAsync(sessionId, ct);
        if (migration is null ||
            !TryResolvePacketPath(migration.RecoveryPacketPath, out var packetPath) ||
            !fileSystem.File.Exists(packetPath))
        {
            return null;
        }

        string content;
        try
        {
            content = await fileSystem.File.ReadAllTextAsync(packetPath, ct);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        var safeOffset = Math.Clamp(offset, 0, content.Length);
        var safeLength = Math.Clamp(
            maxCharacters,
            1,
            MaximumPacketReadCharacters);
        var length = Math.Min(safeLength, content.Length - safeOffset);
        int? nextOffset = safeOffset + length < content.Length
            ? safeOffset + length
            : null;
        return new SessionRecoveryPacketChunk(
            content.Substring(safeOffset, length),
            safeOffset,
            nextOffset,
            content.Length);
    }

    private async ValueTask<SessionMigrationResult> CompleteCreatedSessionAsync(
        SessionMigration migration,
        CancellationToken ct)
    {
        var availability = await copilotSessionManager.CheckSessionAvailabilityAsync(
            migration.ReplacementSessionId,
            ct);
        if (!availability.Checked)
        {
            return await RecordFailedCreationAsync(
                migration,
                $"Narnia could not verify the recovered session before finalization: {availability.Error ?? "availability check failed"}",
                CancellationToken.None);
        }
        if (!availability.Exists)
        {
            return await RecordFailedCreationAsync(
                migration,
                "Copilot did not retain the recovered event stream.",
                CancellationToken.None);
        }

        var safety = resumeSafetyReader.Inspect(migration.SourceSessionId);
        if (safety.Safety != SessionResumeSafety.Resumable)
        {
            return await RecordFailedCreationAsync(
                migration,
                $"The recovered event stream did not pass resume validation: {safety.Reason ?? "missing session.start"}",
                CancellationToken.None);
        }

        var indexedSession = await sessionRepository.GetByIdAsync(
            migration.ReplacementSessionId,
            ct);
        if (indexedSession is null ||
            indexedSession.TurnCount <= migration.BaselineTurnCount ||
            (migration.BaselineUpdatedAt is not null &&
             indexedSession.UpdatedAt <= migration.BaselineUpdatedAt))
        {
            return await RecordFailedCreationAsync(
                migration,
                "Copilot did not append the recovery turn to Chronicle, so Narnia restored the original event stream.",
                CancellationToken.None);
        }

        try
        {
            var completedAt = timeProvider.GetUtcNow();
            if (!await migrationRepository.CompleteAsync(migration.Id, completedAt, ct))
            {
                return new SessionMigrationResult(
                    false,
                    migration,
                    "Narnia could not find the migration record to finalize.");
            }

            var completed = await migrationRepository.GetByIdAsync(migration.Id, ct);
            return new SessionMigrationResult(
                true,
                completed ?? migration with
                {
                    Status = SessionMigrationStatus.Completed,
                    UpdatedAt = completedAt,
                    CompletedAt = completedAt,
                },
                null);
        }
        catch (SqliteException exception)
        {
            var error =
                $"The recovered session is valid, but Narnia could not finalize its recovery record: {exception.Message}";
            return new SessionMigrationResult(false, migration, error);
        }
    }

    private async ValueTask TryMarkFailedAsync(
        string migrationId,
        string error,
        CancellationToken ct)
    {
        try
        {
            await migrationRepository.MarkFailedAsync(
                migrationId,
                error,
                timeProvider.GetUtcNow(),
                ct);
        }
        catch (SqliteException)
        {
        }
    }

    private async ValueTask<string?> ResetIncompleteMigrationAsync(
        SessionMigration migration,
        string successMessage,
        CancellationToken ct)
    {
        if (migration.IsInPlace &&
            !string.IsNullOrWhiteSpace(migration.ArchivedEventsPath) &&
            !string.IsNullOrWhiteSpace(migration.ArchivedEventsSha256))
        {
            var restore = await eventStreamRecovery.RestoreAsync(
                migration.SourceSessionId,
                migration.Id,
                migration.ArchivedEventsPath,
                migration.ArchivedEventsSha256,
                ct);
            if (!restore.Restored)
            {
                var error =
                    $"The in-place migration could not restore its original event stream: {restore.Error}";
                await TryMarkCleanupRequiredAsync(migration.Id, error);
                return error;
            }
        }
        try
        {
            if (!await migrationRepository.ResetAsync(
                migration.Id,
                successMessage,
                timeProvider.GetUtcNow(),
                CancellationToken.None))
            {
                return "Narnia could not find the migration record to reset.";
            }
        }
        catch (SqliteException exception)
        {
            return $"Narnia could not reset the stale migration record: {exception.Message}";
        }

        if (!migration.IsInPlace)
        {
            var cleanupError = await TryRemoveReplacementAsync(
                migration.ReplacementSessionId,
                ct);
            if (cleanupError is not null)
            {
                var error =
                    $"Narnia restored the original references, but could not remove the legacy successor: {cleanupError}";
                await TryMarkCleanupRequiredAsync(migration.Id, error);
                return error;
            }
        }

        TryDeleteRecoveryPacket(migration.RecoveryPacketPath);
        return null;
    }

    private async ValueTask ResetCancelledMigrationAsync(SessionMigration migration)
    {
        await RecordFailedCreationAsync(
            migration,
            "Session migration was cancelled before completion.",
            CancellationToken.None);
    }

    private async ValueTask<SessionMigrationResult> RecordFailedCreationAsync(
        SessionMigration migration,
        string error,
        CancellationToken ct)
    {
        if (migration.IsInPlace)
            return await RecordFailedInPlaceCreationAsync(migration, error, ct);

        var cleanupError = await TryRemoveReplacementAsync(
            migration.ReplacementSessionId,
            ct);
        var now = timeProvider.GetUtcNow();
        if (cleanupError is null)
        {
            await TryMarkFailedAsync(migration.Id, error, CancellationToken.None);
            return new SessionMigrationResult(
                false,
                migration with
                {
                    Status = SessionMigrationStatus.Failed,
                    Error = error,
                    UpdatedAt = now,
                },
                error);
        }

        var cleanupRequiredError =
            $"{error} Narnia could not remove the incomplete successor: {cleanupError}";
        try
        {
            await migrationRepository.MarkCleanupRequiredAsync(
                migration.Id,
                cleanupRequiredError,
                now,
                CancellationToken.None);
        }
        catch (SqliteException)
        {
        }

        return new SessionMigrationResult(
            false,
            migration with
            {
                Status = SessionMigrationStatus.CleanupRequired,
                Error = cleanupRequiredError,
                UpdatedAt = now,
            },
            cleanupRequiredError);
    }

    private async ValueTask<SessionMigrationResult> RecordFailedInPlaceCreationAsync(
        SessionMigration migration,
        string error,
        CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();
        if (string.IsNullOrWhiteSpace(migration.ArchivedEventsPath) ||
            string.IsNullOrWhiteSpace(migration.ArchivedEventsSha256))
        {
            await TryMarkFailedAsync(migration.Id, error, CancellationToken.None);
            return new SessionMigrationResult(
                false,
                migration with
                {
                    Status = SessionMigrationStatus.Failed,
                    Error = error,
                    UpdatedAt = now,
                },
                error);
        }

        var restore = await eventStreamRecovery.RestoreAsync(
            migration.SourceSessionId,
            migration.Id,
            migration.ArchivedEventsPath,
            migration.ArchivedEventsSha256,
            ct);
        if (restore.Restored)
        {
            await TryMarkFailedAsync(migration.Id, error, CancellationToken.None);
            return new SessionMigrationResult(
                false,
                migration with
                {
                    Status = SessionMigrationStatus.Failed,
                    Error = error,
                    UpdatedAt = now,
                },
                error);
        }

        var cleanupRequiredError =
            $"{error} Narnia could not restore the original event stream: {restore.Error}";
        await TryMarkCleanupRequiredAsync(migration.Id, cleanupRequiredError);
        return new SessionMigrationResult(
            false,
            migration with
            {
                Status = SessionMigrationStatus.CleanupRequired,
                Error = cleanupRequiredError,
                UpdatedAt = now,
            },
            cleanupRequiredError);
    }

    private async ValueTask TryMarkCleanupRequiredAsync(
        string migrationId,
        string error)
    {
        try
        {
            await migrationRepository.MarkCleanupRequiredAsync(
                migrationId,
                error,
                timeProvider.GetUtcNow(),
                CancellationToken.None);
        }
        catch (SqliteException)
        {
        }
    }

    private async ValueTask<string?> TryRemoveReplacementAsync(
        string replacementSessionId,
        CancellationToken ct)
    {
        try
        {
            var deletionResults = await copilotSessionManager.DeleteSessionsAsync(
                [replacementSessionId],
                ct);
            var deletion = deletionResults.FirstOrDefault();
            if (deletion?.Deleted == true ||
                string.Equals(
                    deletion?.Error,
                    "Session is not available through the local Copilot SDK runtime.",
                    StringComparison.Ordinal))
            {
                return null;
            }

            return deletion?.Error ?? "Copilot did not report a deletion outcome.";
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
                IOException or
                UnauthorizedAccessException or
                TimeoutException)
        {
            return exception.Message;
        }
    }

    private static bool IsStale(SessionMigration migration, DateTimeOffset now) =>
        now - migration.UpdatedAt >= PreparingStaleAfter;

    private void TryDeleteRecoveryPacket(string storedPath)
    {
        if (!TryResolvePacketPath(storedPath, out var packetPath) ||
            !fileSystem.File.Exists(packetPath))
        {
            return;
        }

        try
        {
            fileSystem.File.Delete(packetPath);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private bool TryResolvePacketPath(string storedPath, out string packetPath)
    {
        var root = fileSystem.Path.GetFullPath(options.RecoveryDirectory)
            .TrimEnd(
                fileSystem.Path.DirectorySeparatorChar,
                fileSystem.Path.AltDirectorySeparatorChar);
        packetPath = fileSystem.Path.GetFullPath(storedPath);
        var relative = fileSystem.Path.GetRelativePath(root, packetPath);
        return relative.Length > 0
            && !relative.Equals("..", StringComparison.Ordinal)
            && !relative.StartsWith(
                $"..{fileSystem.Path.DirectorySeparatorChar}",
                StringComparison.Ordinal)
            && !fileSystem.Path.IsPathRooted(relative);
    }

    private static SessionMigrationReferenceSummary EmptyReferences() =>
        new(false, false, false, 0, 0, 0);
}
