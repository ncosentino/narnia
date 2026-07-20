using NexusLabs.Narnia.Core.Models;

namespace NexusLabs.Narnia.Core.Repositories;

/// <summary>Persists Narnia-owned session-storage measurements and cleanup audit records.</summary>
public interface ISessionStorageRepository
{
    /// <summary>Persists a complete successful filesystem scan.</summary>
    /// <param name="records">Current per-session measurements.</param>
    /// <param name="startedAt">When the scan began.</param>
    /// <param name="completedAt">When the scan completed.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    ValueTask SaveScanAsync(
        IReadOnlyList<SessionStorageRecord> records,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt,
        CancellationToken ct);

    /// <summary>Records a scan-wide failure without discarding the previous successful cache.</summary>
    /// <param name="startedAt">When the failed scan began.</param>
    /// <param name="completedAt">When the failure was recorded.</param>
    /// <param name="error">User-visible failure message.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    ValueTask RecordScanFailureAsync(
        DateTimeOffset startedAt,
        DateTimeOffset completedAt,
        string error,
        CancellationToken ct);

    /// <summary>Gets every current local session-storage measurement.</summary>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>Measurements ordered by descending logical size.</returns>
    ValueTask<IReadOnlyList<SessionStorageRecord>> GetCurrentAsync(CancellationToken ct);

    /// <summary>Gets the current local storage measurement for one session.</summary>
    /// <param name="sessionId">Copilot session identifier.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>The measurement, or <c>null</c> when no local state was measured.</returns>
    ValueTask<SessionStorageRecord?> GetBySessionIdAsync(
        string sessionId,
        CancellationToken ct);

    /// <summary>Gets recent global daily storage snapshots.</summary>
    /// <param name="days">Maximum number of recent calendar days to return.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>Snapshots ordered from oldest to newest.</returns>
    ValueTask<IReadOnlyList<SessionStorageDailySnapshot>> GetDailyAsync(
        int days,
        CancellationToken ct);

    /// <summary>Gets the persisted outcome of the latest completed scan.</summary>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>The latest result, or <c>null</c> before the first completed scan.</returns>
    ValueTask<SessionStorageScanInfo?> GetLastScanAsync(CancellationToken ct);

    /// <summary>Removes deleted sessions from the current measurement cache.</summary>
    /// <param name="sessionIds">Successfully deleted session identifiers.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    ValueTask RemoveCurrentAsync(
        IReadOnlyCollection<string> sessionIds,
        CancellationToken ct);

    /// <summary>Appends cleanup audit entries.</summary>
    /// <param name="entries">Cleanup outcomes to record.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    ValueTask RecordCleanupAsync(
        IReadOnlyCollection<SessionCleanupAuditEntry> entries,
        CancellationToken ct);

    /// <summary>Gets recent cleanup audit entries.</summary>
    /// <param name="limit">Maximum number of entries to return.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>Cleanup outcomes ordered from newest to oldest.</returns>
    ValueTask<IReadOnlyList<SessionCleanupAuditEntry>> GetRecentCleanupAsync(
        int limit,
        CancellationToken ct);
}
