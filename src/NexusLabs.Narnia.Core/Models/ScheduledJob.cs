namespace NexusLabs.Narnia.Core.Models;

/// <summary>Where a skill referenced by a scheduled job is resolved from.</summary>
public enum SkillResolution
{
    /// <summary>Resolution is unknown or not yet determined.</summary>
    Unknown,

    /// <summary>A globally-installed Copilot plugin skill, available regardless of directory.</summary>
    Plugin,

    /// <summary>A repo-local skill, available only when Copilot is launched from the job's directory.</summary>
    RepoLocal,
}

/// <summary>
/// A catalog entry describing a scheduled Copilot job. Narnia is a metadata registry only: Windows
/// Task Scheduler remains the scheduler and executor, and the job's own wrapper script holds the
/// real <c>copilot -p</c> prompt. This record captures the metadata needed to catalog the job, join
/// it to its live scheduled task (via <see cref="TaskFolder"/> + <see cref="TaskName"/>), surface
/// its logs, and correlate the sessions it produces.
/// </summary>
/// <param name="Id">Narnia-assigned stable identifier (also the <c>narnia-job:&lt;id&gt;</c> marker).</param>
/// <param name="Name">User-facing display name.</param>
/// <param name="Description">What the job does.</param>
/// <param name="Cwd">
/// The working directory the job runs in. Required to reproduce jobs that use a repo-local skill,
/// since skill resolution depends on the directory Copilot is launched from.
/// </param>
/// <param name="Cadence">Human-readable cadence copied from the OS trigger (display only; the task owns timing).</param>
/// <param name="Args">Extra arguments passed to the wrapper script (e.g. <c>-Lookback 24h</c>).</param>
/// <param name="ScriptPath">The wrapper script the scheduled task runs; the live prompt is read through to it.</param>
/// <param name="LogDir">The canonical directory the job writes run logs to.</param>
/// <param name="AllowFlags">The Copilot allow-flags the job runs with (e.g. <c>--allow-all-tools --allow-all-paths</c>).</param>
/// <param name="TaskFolder">The Task Scheduler folder the job's task lives in (e.g. <c>\Narnia\</c>).</param>
/// <param name="TaskName">The Task Scheduler task name, used with <paramref name="TaskFolder"/> to join live status.</param>
/// <param name="Notes">Free-form notes.</param>
/// <param name="CreatedAt">When the job was first cataloged.</param>
/// <param name="UpdatedAt">When the job's metadata last changed.</param>
/// <param name="Skills">The skills/plugins the job references, in order.</param>
public sealed record ScheduledJob(
    string Id,
    string Name,
    string? Description,
    string? Cwd,
    string? Cadence,
    string? Args,
    string? ScriptPath,
    string? LogDir,
    string? AllowFlags,
    string TaskFolder,
    string TaskName,
    string? Notes,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<ScheduledJobSkill> Skills,
    string? Prompt = null,
    string? CadenceKind = null,
    string? CadenceTime = null,
    string? CadenceDays = null,
    string? CopilotArgs = null);

/// <summary>A skill or plugin referenced by a <see cref="ScheduledJob"/>, recorded as catalog metadata.</summary>
/// <param name="Skill">The skill or plugin name.</param>
/// <param name="Resolution">Where the skill resolves from.</param>
/// <param name="Order">Zero-based position within the job's skill list.</param>
public sealed record ScheduledJobSkill(
    string Skill,
    SkillResolution Resolution,
    int Order);

/// <summary>
/// The settable metadata of a <see cref="ScheduledJob"/> (everything except the assigned id and
/// timestamps), used as the input for create and update so callers never construct partial records.
/// </summary>
/// <param name="Name">User-facing display name.</param>
/// <param name="Description">What the job does.</param>
/// <param name="Cwd">The working directory the job runs in.</param>
/// <param name="Cadence">Human-readable cadence copied from the OS trigger.</param>
/// <param name="Args">Extra arguments passed to the wrapper script.</param>
/// <param name="ScriptPath">The wrapper script the scheduled task runs.</param>
/// <param name="LogDir">The canonical directory the job writes run logs to.</param>
/// <param name="AllowFlags">The Copilot allow-flags the job runs with.</param>
/// <param name="TaskFolder">The Task Scheduler folder the job's task lives in.</param>
/// <param name="TaskName">The Task Scheduler task name.</param>
/// <param name="Notes">Free-form notes.</param>
/// <param name="Skills">The skills/plugins the job references, in order.</param>
public sealed record ScheduledJobDraft(
    string Name,
    string? Description,
    string? Cwd,
    string? Cadence,
    string? Args,
    string? ScriptPath,
    string? LogDir,
    string? AllowFlags,
    string TaskFolder,
    string TaskName,
    string? Notes,
    IReadOnlyList<ScheduledJobSkill> Skills,
    string? Prompt = null,
    string? CadenceKind = null,
    string? CadenceTime = null,
    string? CadenceDays = null,
    string? CopilotArgs = null);
