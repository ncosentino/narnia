namespace NexusLabs.Narnia.Core.Models;

/// <summary>How often a scheduled task should fire. Normalized so the registrar — not the caller —
/// owns translating to a platform trigger; the catalog never stores raw scheduler syntax.</summary>
public enum ScheduleCadenceKind
{
    /// <summary>Fires once per day at <see cref="ScheduleCadence.TimeOfDay"/>.</summary>
    Daily,

    /// <summary>Fires weekly on <see cref="ScheduleCadence.DaysOfWeek"/> at <see cref="ScheduleCadence.TimeOfDay"/>.</summary>
    Weekly,

    /// <summary>Fires monthly on <see cref="ScheduleCadence.DayOfMonth"/> at <see cref="ScheduleCadence.TimeOfDay"/>.</summary>
    Monthly,
}

/// <summary>A normalized firing cadence. Weekly uses <see cref="DaysOfWeek"/>; Monthly uses
/// <see cref="DayOfMonth"/>; Daily uses neither.</summary>
/// <param name="Kind">Daily, Weekly, or Monthly.</param>
/// <param name="TimeOfDay">Local time of day to fire at.</param>
/// <param name="DaysOfWeek">Days to fire on for a weekly cadence; ignored otherwise.</param>
/// <param name="DayOfMonth">Day of month (1-31) to fire on for a monthly cadence; ignored otherwise.</param>
public sealed record ScheduleCadence(
    ScheduleCadenceKind Kind,
    TimeOnly TimeOfDay,
    IReadOnlyList<DayOfWeek> DaysOfWeek,
    int DayOfMonth = 1)
{
    /// <summary>A short human label (e.g. "Daily 05:00", "Weekly Mon,Fri 05:30", "Monthly day 1 06:00").</summary>
    public string Describe() => Kind switch
    {
        ScheduleCadenceKind.Weekly =>
            $"Weekly {string.Join(",", DaysOfWeek.Select(d => d.ToString()[..3]))} {TimeOfDay:HH\\:mm}",
        ScheduleCadenceKind.Monthly =>
            $"Monthly day {DayOfMonth} {TimeOfDay:HH\\:mm}",
        _ => $"Daily {TimeOfDay:HH\\:mm}",
    };
}

/// <summary>
/// Everything the registrar needs to create a standardized scheduled task: where it lives, what it
/// runs, and how often. Identity is <see cref="Folder"/> + <see cref="Name"/>; the
/// <see cref="JobId"/> is stamped into the task description as the <c>narnia-job:&lt;id&gt;</c>
/// marker so the task is unambiguously recognizable as Narnia's.
/// </summary>
/// <param name="JobId">The catalog id stamped as the recognition marker.</param>
/// <param name="Folder">The scheduler folder (e.g. <c>\Narnia\</c>).</param>
/// <param name="Name">The task name within its folder.</param>
/// <param name="Execute">The executable the task runs (e.g. <c>pwsh.exe</c>).</param>
/// <param name="Arguments">Arguments passed to the executable.</param>
/// <param name="WorkingDirectory">The working directory, or <c>null</c> to inherit.</param>
/// <param name="Cadence">When the task fires.</param>
public sealed record ScheduledTaskRegistration(
    string JobId,
    string Folder,
    string Name,
    string Execute,
    string Arguments,
    string? WorkingDirectory,
    ScheduleCadence Cadence);
