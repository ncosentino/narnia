namespace NexusLabs.Narnia.Core.Models;

/// <summary>
/// Provides shared scheduled-task health classification.
/// </summary>
public static class ScheduledTaskStatusExtensions
{
    /// <summary>
    /// Classifies a live scheduled-task status, treating the running state as authoritative over the
    /// previous run's retained result code.
    /// </summary>
    /// <param name="status">The live task status, or <c>null</c> when the cataloged task is missing.</param>
    /// <returns>A structured health classification suitable for UI and API consumers.</returns>
    public static ScheduledTaskHealthKind GetHealthKind(this ScheduledTaskStatus? status)
    {
        if (status is null)
            return ScheduledTaskHealthKind.Drift;
        if (status.State == ScheduledTaskState.Running)
            return ScheduledTaskHealthKind.Running;
        if (status.State == ScheduledTaskState.Disabled)
            return ScheduledTaskHealthKind.Disabled;

        return status.LastResult switch
        {
            null or 267011 => ScheduledTaskHealthKind.NeverRun,
            0 => ScheduledTaskHealthKind.Succeeded,
            267009 => ScheduledTaskHealthKind.Running,
            267010 => ScheduledTaskHealthKind.Disabled,
            267012 => ScheduledTaskHealthKind.NoMoreRunsScheduled,
            267013 => ScheduledTaskHealthKind.NotFullyScheduled,
            267014 => ScheduledTaskHealthKind.Terminated,
            267015 => ScheduledTaskHealthKind.NoValidTriggers,
            267016 => ScheduledTaskHealthKind.EventTrigger,
            _ => ScheduledTaskHealthKind.Failed,
        };
    }

    /// <summary>
    /// Gets whether the classification represents a failure or catalog-to-scheduler drift that
    /// requires user attention.
    /// </summary>
    /// <param name="kind">The health classification to evaluate.</param>
    /// <returns><c>true</c> for failed or missing scheduled tasks; otherwise <c>false</c>.</returns>
    public static bool RequiresAttention(this ScheduledTaskHealthKind kind) =>
        kind is ScheduledTaskHealthKind.Drift or ScheduledTaskHealthKind.Failed;
}
