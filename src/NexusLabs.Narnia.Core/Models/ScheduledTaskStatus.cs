namespace NexusLabs.Narnia.Core.Models;

/// <summary>The live run state of a scheduled task as reported by the OS scheduler.</summary>
public enum ScheduledTaskState
{
    /// <summary>State could not be determined.</summary>
    Unknown,

    /// <summary>The task is disabled and will not run.</summary>
    Disabled,

    /// <summary>The task is queued to run.</summary>
    Queued,

    /// <summary>The task is enabled and ready to run at its next trigger.</summary>
    Ready,

    /// <summary>The task is currently running.</summary>
    Running,
}

/// <summary>
/// A read-only snapshot of a scheduled task's identity and live status, as observed from the OS
/// scheduler. Narnia never writes timing through this; it is purely for displaying the catalog
/// joined to what the scheduler actually reports.
/// </summary>
/// <param name="TaskFolder">The scheduler folder the task lives in (e.g. <c>\Narnia\</c>).</param>
/// <param name="TaskName">The task name within its folder.</param>
/// <param name="State">The task's live state.</param>
/// <param name="LastRunTime">When the task last ran, or <c>null</c> if it has never run.</param>
/// <param name="LastResult">The task's last exit/result code, or <c>null</c> if unavailable.</param>
/// <param name="NextRunTime">When the task is next scheduled to run, or <c>null</c> if none.</param>
/// <param name="ActionSummary">A short summary of the task's action (executable + arguments).</param>
public sealed record ScheduledTaskStatus(
    string TaskFolder,
    string TaskName,
    ScheduledTaskState State,
    DateTimeOffset? LastRunTime,
    int? LastResult,
    DateTimeOffset? NextRunTime,
    string? ActionSummary);
