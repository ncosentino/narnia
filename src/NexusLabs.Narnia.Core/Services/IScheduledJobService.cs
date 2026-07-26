using NexusLabs.Narnia.Core.Models;

namespace NexusLabs.Narnia.Core.Services;

/// <summary>A skill or plugin a job references, as supplied by a caller creating or updating a job.</summary>
/// <param name="Skill">The skill or plugin name.</param>
/// <param name="Resolution">
/// Where the skill resolves from — parsed leniently into <see cref="SkillResolution"/> (e.g.
/// "plugin", "repolocal"); unrecognized values become <see cref="SkillResolution.Unknown"/>.
/// </param>
public sealed record ScheduledJobSkillInput(string Skill, string? Resolution);

/// <summary>
/// The caller-supplied definition of a Narnia-owned scheduled job, in storage-agnostic terms. This
/// is the single input shape shared by the HTTP endpoints, the MCP tools, and the web UI so none of
/// them re-implement cadence parsing or wrapper generation.
/// </summary>
/// <param name="Name">User-facing display name (required).</param>
/// <param name="Description">What the job does.</param>
/// <param name="Cwd">Working directory the job runs in; required to reproduce repo-local skills.</param>
/// <param name="Prompt">The prompt passed to <c>copilot -p</c> (required — it is what the job runs).</param>
/// <param name="AllowFlags">Copilot allow-flags (e.g. <c>--allow-all-tools --allow-all-paths</c>).</param>
/// <param name="CopilotArgs">Extra arguments appended to the copilot invocation.</param>
/// <param name="TaskName">Scheduler task name; defaults to <paramref name="Name"/> when omitted.</param>
/// <param name="CadenceKind">"daily", "weekly", or "monthly" (parsed leniently; defaults to daily).</param>
/// <param name="Time">Local fire time as "HH:mm" (defaults to 05:00 when unparsable).</param>
/// <param name="Days">Day names for a weekly cadence (e.g. "Monday", "Friday").</param>
/// <param name="DayOfMonth">Day of month (1-31) for a monthly cadence.</param>
/// <param name="Skills">The skills/plugins the job references, in order.</param>
public sealed record ScheduledJobInput(
    string Name,
    string? Description = null,
    string? Cwd = null,
    string? Prompt = null,
    string? AllowFlags = null,
    string? CopilotArgs = null,
    string? TaskName = null,
    string? CadenceKind = null,
    string? Time = null,
    IReadOnlyList<string>? Days = null,
    int? DayOfMonth = null,
    IReadOnlyList<ScheduledJobSkillInput>? Skills = null);

/// <summary>
/// The outcome of a create request. On success it is either a registered job
/// (<see cref="Registered"/> true, <see cref="Job"/> set) or a copy-paste payload
/// (<see cref="Registered"/> false, <see cref="Script"/> + <see cref="Command"/> set). On failure
/// <see cref="Ok"/> is false and <see cref="Error"/> explains why.
/// </summary>
public sealed record ScheduledJobCreateResult(
    bool Ok,
    string? Error,
    bool Registered,
    ScheduledJob? Job,
    string? Script,
    string? Command)
{
    /// <summary>A failed create with a human-readable reason.</summary>
    public static ScheduledJobCreateResult Failure(string error) => new(false, error, false, null, null, null);

    /// <summary>A successful create that registered the OS task.</summary>
    public static ScheduledJobCreateResult Created(ScheduledJob job) => new(true, null, true, job, null, null);

    /// <summary>A successful create that only returned the generated script and registration command.</summary>
    public static ScheduledJobCreateResult CopyPaste(string script, string command) =>
        new(true, null, false, null, script, command);
}

/// <summary>
/// The outcome of an update/enable/run/delete request. <see cref="NotFound"/> distinguishes a
/// missing job from a registrar failure (<see cref="Ok"/> false with <see cref="Error"/>).
/// <see cref="Job"/> carries the affected job when relevant.
/// </summary>
public sealed record ScheduledJobMutationResult(
    bool Ok,
    bool NotFound,
    string? Error,
    ScheduledJob? Job)
{
    /// <summary>The job did not exist.</summary>
    public static ScheduledJobMutationResult Missing { get; } = new(false, true, "Job not found", null);

    /// <summary>The job existed but the operation failed.</summary>
    public static ScheduledJobMutationResult Failure(string error) => new(false, false, error, null);

    /// <summary>The operation succeeded, optionally carrying the affected job.</summary>
    public static ScheduledJobMutationResult Succeeded(ScheduledJob? job = null) => new(true, false, null, job);
}

/// <summary>A cataloged job joined to the live status of its OS scheduled task.</summary>
/// <param name="Job">The cataloged job.</param>
/// <param name="Status">The live task status, or <c>null</c> when no matching task was found.</param>
/// <param name="TaskFound">Whether a live task was found for the job.</param>
public sealed record ScheduledJobStatusView(ScheduledJob Job, ScheduledTaskStatus? Status, bool TaskFound);

/// <summary>The full scheduled-job listing: cataloged jobs joined to status, plus untracked tasks.</summary>
/// <param name="SchedulerSupported">Whether the OS scheduler can be inspected on this platform.</param>
/// <param name="Jobs">Cataloged jobs, each joined to its live task status.</param>
/// <param name="Untracked">Tasks in Narnia's scheduler folder with no matching catalog entry.</param>
public sealed record ScheduledJobListView(
    bool SchedulerSupported,
    IReadOnlyList<ScheduledJobStatusView> Jobs,
    IReadOnlyList<ScheduledTaskStatus> Untracked);

/// <summary>The content of a job's most recent per-run log, or why none is available.</summary>
/// <param name="JobNotFound">Whether the job id itself does not exist in the catalog.</param>
/// <param name="Found">Whether a log file exists for the job (always false when <see cref="JobNotFound"/> is true).</param>
/// <param name="Path">The log file's full path, when <see cref="Found"/> is true.</param>
/// <param name="Content">The log content (possibly truncated to the most recent portion).</param>
/// <param name="Truncated">Whether <see cref="Content"/> was truncated because the file was large.</param>
/// <param name="IsRunning">
/// Whether the task is currently executing, per the OS scheduler's live state — the log for a
/// running job is necessarily incomplete, so callers can poll and keep the reader informed
/// instead of presenting a partial log as if it were a finished (and possibly failed) run.
/// </param>
public sealed record ScheduledJobLogView(bool JobNotFound, bool Found, string? Path, string? Content, bool Truncated, bool IsRunning)
{
    /// <summary>The job id does not exist in the catalog.</summary>
    public static ScheduledJobLogView Missing { get; } = new(true, false, null, null, false, false);

    /// <summary>The job exists but has never run, so no log file exists yet.</summary>
    public static ScheduledJobLogView NoLogYet(bool isRunning) => new(false, false, null, null, false, isRunning);

    /// <summary>The job's most recent log content.</summary>
    public static ScheduledJobLogView Of(string path, string content, bool truncated, bool isRunning) =>
        new(false, true, path, content, truncated, isRunning);
}

/// <summary>
/// The single orchestration point for Narnia-owned scheduled jobs. It owns cadence parsing, wrapper
/// generation, catalog persistence, and OS task registration so that every caller — the HTTP API,
/// the MCP tools, and the web UI — shares one code path and cannot drift.
/// </summary>
public interface IScheduledJobService
{
    /// <summary>Whether the current platform can register/modify scheduled tasks.</summary>
    bool RegistrarSupported { get; }

    /// <summary>Lists all cataloged jobs joined to live task status, plus any untracked Narnia tasks.</summary>
    ValueTask<ScheduledJobListView> ListAsync(CancellationToken ct = default);

    /// <summary>Returns a single cataloged job by id, or <c>null</c> if it does not exist.</summary>
    ValueTask<ScheduledJob?> GetAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// Creates a Narnia-owned job. When <paramref name="register"/> is true the OS task is registered
    /// and the workspace script written; otherwise the generated script and registration command are
    /// returned for the caller to run manually.
    /// </summary>
    ValueTask<ScheduledJobCreateResult> CreateAsync(ScheduledJobInput input, bool register, CancellationToken ct = default);

    /// <summary>
    /// Creates and catalogs a Narnia-owned job whose OS task is disabled atomically during
    /// registration. This is the safe materialization path for imported definitions.
    /// </summary>
    ValueTask<ScheduledJobCreateResult> CreateDisabledAsync(
        ScheduledJobInput input,
        CancellationToken ct);

    /// <summary>Updates an existing job's definition and re-registers its OS task in place.</summary>
    ValueTask<ScheduledJobMutationResult> UpdateAsync(string id, ScheduledJobInput input, CancellationToken ct = default);

    /// <summary>Enables or disables a job's OS task.</summary>
    ValueTask<ScheduledJobMutationResult> SetEnabledAsync(string id, bool enabled, CancellationToken ct = default);

    /// <summary>Starts a job's OS task immediately.</summary>
    ValueTask<ScheduledJobMutationResult> RunAsync(string id, CancellationToken ct = default);

    /// <summary>Deletes a job: its OS task, its generated workspace, and its catalog entry.</summary>
    ValueTask<ScheduledJobMutationResult> DeleteAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// Reads the most recent per-run log for a job (tail-truncated if large), so a failed run can be
    /// diagnosed without leaving the UI.
    /// </summary>
    ValueTask<ScheduledJobLogView> GetLatestLogAsync(string id, CancellationToken ct = default);
}
