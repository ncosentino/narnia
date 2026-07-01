using NexusLabs.Narnia.Core.Models;

namespace NexusLabs.Narnia.Core.Repositories;

/// <summary>
/// Persists the catalog of scheduled Copilot jobs in the Narnia settings database. This is a
/// metadata registry only: it never schedules or runs anything (Windows Task Scheduler owns that).
/// Each entry records enough to catalog a job, join it to its live scheduled task, surface its
/// logs, and correlate the sessions it produces.
/// </summary>
public interface IScheduledJobRegistry
{
    /// <summary>Returns all cataloged jobs, most recently updated first, each with its skills in order.</summary>
    ValueTask<IReadOnlyList<ScheduledJob>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Returns a single job by id, or <c>null</c> if it does not exist.</summary>
    ValueTask<ScheduledJob?> GetByIdAsync(string id, CancellationToken ct = default);

    /// <summary>Creates a new cataloged job from the given metadata.</summary>
    /// <param name="draft">The job metadata to store.</param>
    /// <param name="now">The current timestamp, used for both created and updated.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The created job, including its assigned id.</returns>
    ValueTask<ScheduledJob> CreateAsync(
        ScheduledJobDraft draft,
        DateTimeOffset now,
        CancellationToken ct = default);

    /// <summary>
    /// Creates a job with a caller-supplied id, so a Narnia-authored job's workspace folder, task
    /// marker, and catalog row can all share one id chosen up front.
    /// </summary>
    ValueTask<ScheduledJob> CreateWithIdAsync(
        string id,
        ScheduledJobDraft draft,
        DateTimeOffset now,
        CancellationToken ct = default);

    /// <summary>
    /// Replaces the metadata of an existing job with <paramref name="draft"/> and refreshes its
    /// updated timestamp. Does nothing if no job has the given id.
    /// </summary>
    ValueTask UpdateAsync(
        string id,
        ScheduledJobDraft draft,
        DateTimeOffset now,
        CancellationToken ct = default);

    /// <summary>Deletes a job and its skills.</summary>
    ValueTask DeleteAsync(string id, CancellationToken ct = default);
}
