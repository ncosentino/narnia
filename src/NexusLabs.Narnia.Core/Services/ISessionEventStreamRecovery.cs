using NexusLabs.Narnia.Core.Models;

namespace NexusLabs.Narnia.Core.Services;

/// <summary>Performs narrowly scoped, reversible event-stream rollover for broken sessions.</summary>
public interface ISessionEventStreamRecovery
{
    /// <summary>Validates and hashes the current stream without modifying it.</summary>
    /// <param name="sessionId">Broken Copilot session identifier.</param>
    /// <param name="migrationId">Narnia migration identifier used in the archive name.</param>
    /// <param name="ct">Cancellation token for read-only planning.</param>
    /// <returns>Deterministic archive path and integrity hash.</returns>
    ValueTask<SessionEventArchivePlanResult> PlanAsync(
        string sessionId,
        string migrationId,
        CancellationToken ct);

    /// <summary>Atomically archives the current event stream using a persisted plan.</summary>
    /// <param name="sessionId">Broken Copilot session identifier.</param>
    /// <param name="archivePath">Previously planned archive path.</param>
    /// <param name="expectedSha256">Previously recorded source hash.</param>
    /// <param name="ct">Cancellation token before filesystem mutation begins.</param>
    /// <returns>Explicit archival result.</returns>
    ValueTask<SessionEventArchiveResult> ArchiveAsync(
        string sessionId,
        string archivePath,
        string expectedSha256,
        CancellationToken ct);

    /// <summary>Restores the archived original and retains any failed replacement event stream.</summary>
    /// <param name="sessionId">Copilot session identifier.</param>
    /// <param name="migrationId">Narnia migration identifier used in failure filenames.</param>
    /// <param name="archivePath">Archived original event-stream path.</param>
    /// <param name="expectedSha256">Expected archived original hash.</param>
    /// <param name="ct">Cancellation token before filesystem mutation begins.</param>
    /// <returns>Explicit rollback result.</returns>
    ValueTask<SessionEventRestoreResult> RestoreAsync(
        string sessionId,
        string migrationId,
        string archivePath,
        string expectedSha256,
        CancellationToken ct);
}
