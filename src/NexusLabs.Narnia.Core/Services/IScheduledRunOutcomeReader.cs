using NexusLabs.Narnia.Core.Models;

namespace NexusLabs.Narnia.Core.Services;

/// <summary>
/// Recovers how a scheduled job's most recent run actually ended, which the OS scheduler's exit
/// code cannot express on its own.
/// </summary>
public interface IScheduledRunOutcomeReader
{
    /// <summary>
    /// Reads the outcome of a job's most recent completed run.
    /// </summary>
    /// <param name="jobId">The cataloged job identifier.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>
    /// The recovered outcome, or <see cref="ScheduledRunOutcome.Indeterminate"/> when the run cannot
    /// be inspected. Never throws for missing or unreadable files: an unknown outcome must never
    /// stop a job's status from being listed.
    /// </returns>
    ValueTask<ScheduledRunOutcome> ReadLatestAsync(string jobId, CancellationToken ct = default);
}
