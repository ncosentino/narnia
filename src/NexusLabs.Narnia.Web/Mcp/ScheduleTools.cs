using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using NexusLabs.Narnia.Core.Models;
using NexusLabs.Narnia.Core.Services;

namespace NexusLabs.Narnia.Web.Mcp;

/// <summary>
/// MCP tools for Narnia-owned scheduled Copilot jobs (create/read/update/enable/run/delete). These
/// are thin wrappers over <see cref="IScheduledJobService"/> — the same service backing the HTTP
/// API and the web UI — so an agent (this CLI, or any other client of the shared MCP endpoint) can
/// register and manage scheduled jobs without shelling out to the HTTP API itself.
/// </summary>
[McpServerToolType]
internal sealed class ScheduleTools(IScheduledJobService jobService)
{
    [McpServerTool(Name = "list_schedules")]
    [Description("Lists every Narnia-cataloged scheduled Copilot job joined to its live Windows Task Scheduler status (state, last run, next run), plus any tasks found in Narnia's scheduler folder that are not cataloged.")]
    public async Task<string> ListSchedulesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var view = await jobService.ListAsync(cancellationToken);
            var dto = new ScheduleListMcpDto(
                view.SchedulerSupported,
                view.Jobs.Select(v => new ScheduleJobStatusMcpDto(
                    ToDto(v.Job), v.TaskFound, v.Status is null ? null : ToDto(v.Status))).ToList(),
                view.Untracked.Select(ToDto).ToList());
            return JsonSerializer.Serialize(dto, McpJsonContext.Default.ScheduleListMcpDto);
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    [McpServerTool(Name = "get_schedule")]
    [Description("Gets a single Narnia-cataloged scheduled job by id, including its full prompt and cadence. Use list_schedules first to find the id, or to see live task status.")]
    public async Task<string> GetScheduleAsync(
        [Description("The job's Narnia id, from list_schedules.")] string id,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var job = await jobService.GetAsync(id, cancellationToken);
            if (job is null)
                return $"Error: no scheduled job with id '{id}'.";
            return JsonSerializer.Serialize(ToDto(job), McpJsonContext.Default.ScheduleJobMcpDto);
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    [McpServerTool(Name = "create_schedule")]
    [Description("""
        Creates a Narnia-owned scheduled Copilot job. Narnia generates a self-contained wrapper
        script that runs `copilot -p` with the given prompt on the given cadence, and never edits
        the user's own scripts. Prefer register=true so the Windows scheduled task is created
        immediately; use register=false only to hand back the generated script and registration
        command for the caller to run manually. The prompt IS the job: name the skill to invoke and
        say exactly what to do with its output (e.g. write a file, then call a specific script for
        any deterministic follow-up like a database write or an email) -- there is no hidden wrapper
        behavior beyond what the prompt says. Prefer a self-contained prompt/skill (one that resolves
        its own secrets, e.g. from a repo .env) over relying on injected environment variables, since
        the job runs as a plain `copilot -p` with no pre-injected environment.
        """)]
    public async Task<string> CreateScheduleAsync(
        [Description("Display name for the job.")] string name,
        [Description("The full prompt passed to `copilot -p`. This is what the job does.")] string prompt,
        [Description("Working directory the job runs in. Required when the prompt depends on a repo-local skill.")] string? cwd = null,
        [Description("Short description of what the job does, for the catalog.")] string? description = null,
        [Description("'daily', 'weekly', or 'monthly'. Defaults to 'daily'.")] string cadenceKind = "daily",
        [Description("Local fire time as 24-hour HH:mm. Defaults to '05:00'.")] string time = "05:00",
        [Description("Day names for a weekly cadence, e.g. [\"Monday\",\"Friday\"]. Ignored for other cadences.")] string[]? days = null,
        [Description("Day of month (1-31) for a monthly cadence. Ignored for other cadences.")] int? dayOfMonth = null,
        [Description("Copilot allow-flags. Defaults to '--allow-all-tools --allow-all-paths'.")] string? allowFlags = "--allow-all-tools --allow-all-paths",
        [Description("Extra arguments appended to the copilot invocation.")] string? copilotArgs = null,
        [Description("Skills/plugins this job's prompt invokes, in order -- recorded in the catalog for documentation only.")] ScheduleSkillMcpInput[]? skills = null,
        [Description("When true (default), registers the Windows scheduled task now. When false, only returns the generated script and the command to register it manually.")] bool register = true,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var input = ToInput(name, prompt, cwd, description, cadenceKind, time, days, dayOfMonth, allowFlags, copilotArgs, skills);
            var result = await jobService.CreateAsync(input, register, cancellationToken);
            if (!result.Ok)
                return $"Error: {result.Error}";

            var dto = new ScheduleCreateMcpDto(result.Registered, result.Job?.Id, result.Script, result.Command);
            return JsonSerializer.Serialize(dto, McpJsonContext.Default.ScheduleCreateMcpDto);
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    [McpServerTool(Name = "update_schedule")]
    [Description("Updates an existing Narnia-owned job, regenerates its wrapper script, and re-registers its Windows scheduled task in place. Every field is replaced -- call get_schedule first and pass through anything you don't want to change.")]
    public async Task<string> UpdateScheduleAsync(
        [Description("The job's Narnia id.")] string id,
        [Description("Display name for the job.")] string name,
        [Description("The full prompt passed to `copilot -p`.")] string prompt,
        [Description("Working directory the job runs in.")] string? cwd = null,
        [Description("Short description of what the job does.")] string? description = null,
        [Description("'daily', 'weekly', or 'monthly'.")] string cadenceKind = "daily",
        [Description("Local fire time as 24-hour HH:mm.")] string time = "05:00",
        [Description("Day names for a weekly cadence.")] string[]? days = null,
        [Description("Day of month (1-31) for a monthly cadence.")] int? dayOfMonth = null,
        [Description("Copilot allow-flags.")] string? allowFlags = "--allow-all-tools --allow-all-paths",
        [Description("Extra arguments appended to the copilot invocation.")] string? copilotArgs = null,
        [Description("Skills/plugins this job's prompt invokes, in order.")] ScheduleSkillMcpInput[]? skills = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var input = ToInput(name, prompt, cwd, description, cadenceKind, time, days, dayOfMonth, allowFlags, copilotArgs, skills);
            var result = await jobService.UpdateAsync(id, input, cancellationToken);
            if (result.NotFound)
                return $"Error: no scheduled job with id '{id}'.";
            if (!result.Ok)
                return $"Error: {result.Error}";

            return JsonSerializer.Serialize(new ScheduleMutationMcpDto(true, id), McpJsonContext.Default.ScheduleMutationMcpDto);
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    [McpServerTool(Name = "set_schedule_enabled")]
    [Description("Enables or disables a scheduled job's Windows scheduled task without deleting it. Prefer this over delete_schedule when a job might be needed again later.")]
    public async Task<string> SetScheduleEnabledAsync(
        [Description("The job's Narnia id.")] string id,
        [Description("True to enable, false to disable.")] bool enabled,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await jobService.SetEnabledAsync(id, enabled, cancellationToken);
            if (result.NotFound)
                return $"Error: no scheduled job with id '{id}'.";
            if (!result.Ok)
                return $"Error: {result.Error}";

            return JsonSerializer.Serialize(new ScheduleMutationMcpDto(true, id), McpJsonContext.Default.ScheduleMutationMcpDto);
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    [McpServerTool(Name = "run_schedule_now")]
    [Description("Starts a scheduled job's Windows scheduled task immediately, out of band from its normal cadence. Use this to test a job right after creating or updating it.")]
    public async Task<string> RunScheduleNowAsync(
        [Description("The job's Narnia id.")] string id,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await jobService.RunAsync(id, cancellationToken);
            if (result.NotFound)
                return $"Error: no scheduled job with id '{id}'.";
            if (!result.Ok)
                return $"Error: {result.Error}";

            return JsonSerializer.Serialize(new ScheduleMutationMcpDto(true, id), McpJsonContext.Default.ScheduleMutationMcpDto);
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    [McpServerTool(Name = "delete_schedule")]
    [Description("Deletes a scheduled job: removes its Windows scheduled task, its generated wrapper script, and its catalog entry. This cannot be undone -- prefer set_schedule_enabled(false) if the job might be needed again.")]
    public async Task<string> DeleteScheduleAsync(
        [Description("The job's Narnia id.")] string id,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await jobService.DeleteAsync(id, cancellationToken);
            if (result.NotFound)
                return $"Error: no scheduled job with id '{id}'.";

            return JsonSerializer.Serialize(new ScheduleMutationMcpDto(true, id), McpJsonContext.Default.ScheduleMutationMcpDto);
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    [McpServerTool(Name = "get_schedule_log")]
    [Description("Reads the most recent per-run log for a scheduled job, so a failed run (see list_schedules' status.lastResult) can be diagnosed. Content may be truncated to the most recent portion for very large logs.")]
    public async Task<string> GetScheduleLogAsync(
        [Description("The job's Narnia id.")] string id,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var log = await jobService.GetLatestLogAsync(id, cancellationToken);
            if (log.JobNotFound)
                return $"Error: no scheduled job with id '{id}'.";

            return JsonSerializer.Serialize(
                new ScheduleLogMcpDto(log.Found, log.Path, log.Content, log.Truncated),
                McpJsonContext.Default.ScheduleLogMcpDto);
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    private static ScheduledJobInput ToInput(
        string name, string prompt, string? cwd, string? description, string cadenceKind, string time,
        string[]? days, int? dayOfMonth, string? allowFlags, string? copilotArgs, ScheduleSkillMcpInput[]? skills) =>
        new(
            Name: name, Description: description, Cwd: cwd, Prompt: prompt, AllowFlags: allowFlags,
            CopilotArgs: copilotArgs, CadenceKind: cadenceKind, Time: time, Days: days, DayOfMonth: dayOfMonth,
            Skills: skills?.Select(s => new ScheduledJobSkillInput(s.Skill, s.Resolution)).ToList());

    private static ScheduleJobMcpDto ToDto(ScheduledJob job) => new(
        job.Id, job.Name, job.Description, job.Cwd, job.Prompt, job.CadenceKind, job.CadenceTime, job.CadenceDays,
        job.Cadence, job.AllowFlags, job.CopilotArgs, job.TaskFolder, job.TaskName, job.ScriptPath, job.LogDir,
        job.CreatedAt, job.UpdatedAt,
        job.Skills.Select(s => new ScheduleSkillMcpDto(s.Skill, s.Resolution.ToString().ToLowerInvariant())).ToList());

    private static ScheduleStatusMcpDto ToDto(ScheduledTaskStatus s) => new(
        s.TaskFolder, s.TaskName, s.State.ToString().ToLowerInvariant(), s.LastRunTime, s.LastResult, s.NextRunTime,
        s.ActionSummary);
}

/// <summary>A skill/plugin reference supplied when creating or updating a job via MCP.</summary>
internal sealed record ScheduleSkillMcpInput(
    [property: Description("The skill or plugin name, e.g. 'devleader-blog-newsletter'.")] string Skill,
    [property: Description("Where the skill resolves from: 'plugin' (globally installed) or 'repolocal' (only when Copilot is launched from the job's cwd).")] string? Resolution);

/// <summary>A skill/plugin reference as recorded in a job's catalog entry.</summary>
internal sealed record ScheduleSkillMcpDto(string Skill, string Resolution);

/// <summary>The catalog entry for a scheduled job (no live task status — see <see cref="ScheduleJobStatusMcpDto"/>).</summary>
internal sealed record ScheduleJobMcpDto(
    string Id,
    string Name,
    string? Description,
    string? Cwd,
    string? Prompt,
    string? CadenceKind,
    string? CadenceTime,
    string? CadenceDays,
    string? Cadence,
    string? AllowFlags,
    string? CopilotArgs,
    string TaskFolder,
    string TaskName,
    string? ScriptPath,
    string? LogDir,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<ScheduleSkillMcpDto> Skills);

/// <summary>The live status of a scheduled job's Windows scheduled task.</summary>
internal sealed record ScheduleStatusMcpDto(
    string TaskFolder,
    string TaskName,
    string State,
    DateTimeOffset? LastRunTime,
    int? LastResult,
    DateTimeOffset? NextRunTime,
    string? ActionSummary);

/// <summary>A cataloged job joined to its live task status, as returned by list_schedules.</summary>
internal sealed record ScheduleJobStatusMcpDto(ScheduleJobMcpDto Job, bool TaskFound, ScheduleStatusMcpDto? Status);

/// <summary>The full result of list_schedules: cataloged jobs joined to status, plus untracked tasks.</summary>
internal sealed record ScheduleListMcpDto(
    bool SchedulerSupported,
    IReadOnlyList<ScheduleJobStatusMcpDto> Jobs,
    IReadOnlyList<ScheduleStatusMcpDto> Untracked);

/// <summary>The result of create_schedule: either a registered job id, or a copy-paste script + command.</summary>
internal sealed record ScheduleCreateMcpDto(bool Registered, string? Id, string? Script, string? Command);

/// <summary>The result of update_schedule, set_schedule_enabled, run_schedule_now, or delete_schedule.</summary>
internal sealed record ScheduleMutationMcpDto(bool Ok, string Id);

/// <summary>The result of get_schedule_log.</summary>
internal sealed record ScheduleLogMcpDto(bool Found, string? Path, string? Content, bool Truncated);
