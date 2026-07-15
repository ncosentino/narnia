namespace NexusLabs.Narnia.Core.Models;

/// <summary>
/// Classifies a scheduled task's live state and last result without presentation-specific text.
/// </summary>
public enum ScheduledTaskHealthKind
{
    /// <summary>No matching scheduled task was found for a cataloged job.</summary>
    Drift,

    /// <summary>The task is currently executing.</summary>
    Running,

    /// <summary>The task has not completed a run yet.</summary>
    NeverRun,

    /// <summary>The task's most recent run completed successfully.</summary>
    Succeeded,

    /// <summary>The scheduler reports that the task is disabled.</summary>
    Disabled,

    /// <summary>The scheduler reports that no more runs are scheduled.</summary>
    NoMoreRunsScheduled,

    /// <summary>The scheduler reports that the task is not fully scheduled.</summary>
    NotFullyScheduled,

    /// <summary>The scheduler reports that the task was terminated.</summary>
    Terminated,

    /// <summary>The scheduler reports that the task has no valid triggers.</summary>
    NoValidTriggers,

    /// <summary>The scheduler reports an event-trigger status.</summary>
    EventTrigger,

    /// <summary>The task's most recent run returned a failure code.</summary>
    Failed,
}
