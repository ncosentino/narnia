using NexusLabs.Narnia.Core.Models;

namespace NexusLabs.Narnia.Core.Repositories;

/// <summary>
/// Persists provenance for schedule-package imports in Narnia's settings database. Queries only
/// return records whose destination scheduled job still exists, so deleting a job makes its old
/// provenance eligible for a later re-import without modifying the Copilot session store.
/// </summary>
public interface IScheduledJobImportRepository
{
    /// <summary>Returns provenance records for the supplied active local job IDs.</summary>
    ValueTask<IReadOnlyList<ScheduledJobImportRecord>> GetByJobIdsAsync(
        IReadOnlyCollection<string> jobIds,
        CancellationToken ct);

    /// <summary>Returns active imports for one package-local job identity.</summary>
    ValueTask<IReadOnlyList<ScheduledJobImportRecord>> GetActiveAsync(
        string packageId,
        string portableJobId,
        CancellationToken ct);

    /// <summary>Records the provenance of a successfully imported local job.</summary>
    ValueTask AddAsync(
        ScheduledJobImportRecord record,
        CancellationToken ct);

    /// <summary>Removes provenance for a local job during import rollback.</summary>
    ValueTask DeleteAsync(
        string jobId,
        CancellationToken ct);
}
