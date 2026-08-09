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
    /// Classifies a live scheduled-task status together with how its most recent run actually
    /// ended.
    /// </summary>
    /// <param name="status">The live task status, or <c>null</c> when the cataloged task is missing.</param>
    /// <param name="outcome">The recovered outcome of the most recent run, if it could be read.</param>
    /// <returns>A structured health classification suitable for UI and API consumers.</returns>
    /// <remarks>
    /// This only ever downgrades success. The Copilot CLI shuts down gracefully when interrupted, so
    /// a killed run still exits 0 and the scheduler still reports success; every other
    /// classification already comes from the scheduler's own state and is left alone.
    /// </remarks>
    public static ScheduledTaskHealthKind GetHealthKind(
        this ScheduledTaskStatus? status,
        ScheduledRunOutcome? outcome)
    {
        var kind = status.GetHealthKind();
        return kind == ScheduledTaskHealthKind.Succeeded && outcome?.WasInterrupted == true
            ? ScheduledTaskHealthKind.Interrupted
            : kind;
    }

    /// <summary>
    /// Gets whether the classification represents a failure or catalog-to-scheduler drift that
    /// requires user attention.
    /// </summary>
    /// <param name="kind">The health classification to evaluate.</param>
    /// <returns><c>true</c> for failed, interrupted, or missing scheduled tasks; otherwise <c>false</c>.</returns>
    public static bool RequiresAttention(this ScheduledTaskHealthKind kind) =>
        kind is ScheduledTaskHealthKind.Drift
            or ScheduledTaskHealthKind.Failed
            or ScheduledTaskHealthKind.Interrupted;
}
