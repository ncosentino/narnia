using NexusLabs.Narnia.Core.Models;

namespace NexusLabs.Narnia.Core.Services;

/// <summary>
/// Reads scheduled tasks and their live status from the OS scheduler (e.g. Windows Task Scheduler).
/// Read-only by design: Narnia is a metadata registry and never schedules, edits, or runs tasks
/// through this seam. Implementations are platform-specific; unsupported platforms report nothing.
/// </summary>
public interface IScheduledTaskProvider
{
    /// <summary>Whether the OS scheduler can be read on the current platform.</summary>
    bool IsSupported { get; }

    /// <summary>
    /// Returns the live status of every task directly within <paramref name="folder"/>, or an
    /// empty list when the folder does not exist or the platform is unsupported.
    /// </summary>
    /// <param name="folder">The scheduler folder to enumerate (e.g. <c>\Narnia\</c>).</param>
    /// <param name="ct">A cancellation token.</param>
    ValueTask<IReadOnlyList<ScheduledTaskStatus>> ListInFolderAsync(
        string folder, CancellationToken ct = default);

    /// <summary>
    /// Returns the live status of a single task identified by folder and name, or <c>null</c> when
    /// it does not exist or the platform is unsupported.
    /// </summary>
    /// <param name="folder">The scheduler folder the task lives in (e.g. <c>\Narnia\</c>).</param>
    /// <param name="name">The task name within that folder.</param>
    /// <param name="ct">A cancellation token.</param>
    ValueTask<ScheduledTaskStatus?> GetAsync(
        string folder, string name, CancellationToken ct = default);
}
