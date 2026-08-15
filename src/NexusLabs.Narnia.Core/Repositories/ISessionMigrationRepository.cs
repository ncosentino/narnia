using NexusLabs.Narnia.Core.Models;

namespace NexusLabs.Narnia.Core.Repositories;

/// <summary>Persists Narnia-owned recovery relationships and carries references forward atomically.</summary>
public interface ISessionMigrationRepository
{
    /// <summary>Gets the most recent migration whose source matches the requested session.</summary>
    /// <param name="sourceSessionId">Original Copilot session identifier.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>The latest migration, or <c>null</c>.</returns>
    ValueTask<SessionMigration?> GetLatestBySourceAsync(
        string sourceSessionId,
        CancellationToken ct);

    /// <summary>Gets the most recent migration that created the requested replacement session.</summary>
    /// <param name="replacementSessionId">Replacement Copilot session identifier.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>The migration, or <c>null</c>.</returns>
    ValueTask<SessionMigration?> GetByReplacementAsync(
        string replacementSessionId,
        CancellationToken ct);

    /// <summary>Gets a migration by its Narnia identifier.</summary>
    /// <param name="migrationId">Narnia migration identifier.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>The migration, or <c>null</c>.</returns>
    ValueTask<SessionMigration?> GetByIdAsync(string migrationId, CancellationToken ct);

    /// <summary>Counts Narnia references that migration will carry forward.</summary>
    /// <param name="sourceSessionId">Original Copilot session identifier.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>Reference summary for preview and audit.</returns>
    ValueTask<SessionMigrationReferenceSummary> GetReferenceSummaryAsync(
        string sourceSessionId,
        CancellationToken ct);

    /// <summary>Persists a new migration before Copilot session creation begins.</summary>
    /// <param name="migration">Preparing migration record.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    ValueTask AddAsync(SessionMigration migration, CancellationToken ct);

    /// <summary>Restarts a failed in-place migration using its existing record.</summary>
    /// <param name="migration">Preparing replacement values for the existing record.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns><c>true</c> when the migration existed and was restarted.</returns>
    ValueTask<bool> RestartAsync(SessionMigration migration, CancellationToken ct);

    /// <summary>Marks that Copilot created and seeded the replacement session.</summary>
    /// <param name="migrationId">Narnia migration identifier.</param>
    /// <param name="updatedAt">Status timestamp.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    ValueTask MarkSessionCreatedAsync(
        string migrationId,
        DateTimeOffset updatedAt,
        CancellationToken ct);

    /// <summary>Marks that an incomplete successor may still exist and requires cleanup.</summary>
    /// <param name="migrationId">Narnia migration identifier.</param>
    /// <param name="error">Cleanup failure detail.</param>
    /// <param name="updatedAt">Status timestamp.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    ValueTask MarkCleanupRequiredAsync(
        string migrationId,
        string error,
        DateTimeOffset updatedAt,
        CancellationToken ct);

    /// <summary>
    /// Atomically completes the migration, clones source overrides, adds Collection membership,
    /// and replaces source references in saved windows.
    /// </summary>
    /// <param name="migrationId">Narnia migration identifier.</param>
    /// <param name="completedAt">Completion timestamp.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns><c>true</c> when the migration record existed and completed.</returns>
    ValueTask<bool> CompleteAsync(
        string migrationId,
        DateTimeOffset completedAt,
        CancellationToken ct);

    /// <summary>Records an explicit migration failure without deleting source history.</summary>
    /// <param name="migrationId">Narnia migration identifier.</param>
    /// <param name="error">Failure detail.</param>
    /// <param name="updatedAt">Failure timestamp.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    ValueTask MarkFailedAsync(
        string migrationId,
        string error,
        DateTimeOffset updatedAt,
        CancellationToken ct);

    /// <summary>
    /// Reverses carried-forward Narnia references and marks a migration failed so it can be retried.
    /// </summary>
    /// <param name="migrationId">Narnia migration identifier.</param>
    /// <param name="reason">Reset reason recorded on the migration.</param>
    /// <param name="updatedAt">Reset timestamp.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns><c>true</c> when the migration existed and was reset.</returns>
    ValueTask<bool> ResetAsync(
        string migrationId,
        string reason,
        DateTimeOffset updatedAt,
        CancellationToken ct);

    /// <summary>Gets source and successor sessions protected by recorded recovery state.</summary>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>Case-insensitive protected session identifiers.</returns>
    ValueTask<HashSet<string>> GetRecoveryProtectedSessionIdsAsync(CancellationToken ct);
}
