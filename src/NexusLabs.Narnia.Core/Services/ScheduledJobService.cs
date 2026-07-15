using NexusLabs.Narnia.Core.Models;
using NexusLabs.Narnia.Core.Repositories;

namespace NexusLabs.Narnia.Core.Services;

/// <summary>
/// Default <see cref="IScheduledJobService"/> that composes the catalog registry, the OS task
/// registrar, the on-disk job workspace, and the task provider. Narnia is still not a scheduler:
/// this only writes tasks the OS owns, and always generates its own wrapper script so a job's
/// <c>copilot -p</c> prompt lives in Narnia's app-data folder rather than a user's own file.
/// </summary>
public sealed class ScheduledJobService(
    IScheduledJobRegistry registry,
    IScheduledTaskRegistrar registrar,
    IScheduledJobWorkspace workspace,
    IScheduledTaskProvider taskProvider,
    IPowerShellHostResolver hostResolver,
    INarniaSettingsRepository settingsRepository) : IScheduledJobService
{
    private const string NarniaFolder = @"\Narnia\";
    private const int MaxLogChars = 100_000;
    private const string DefaultCopilotCommand = "copilot";

    /// <inheritdoc />
    public bool RegistrarSupported => registrar.IsSupported;

    /// <inheritdoc />
    public async ValueTask<ScheduledJobListView> ListAsync(CancellationToken ct = default)
    {
        var jobs = await registry.GetAllAsync(ct);
        var narniaTasks = await taskProvider.ListInFolderAsync(NarniaFolder, ct);

        var tasksByKey = new Dictionary<string, ScheduledTaskStatus>(StringComparer.OrdinalIgnoreCase);
        foreach (var task in narniaTasks)
            tasksByKey[TaskKey(task.TaskFolder, task.TaskName)] = task;

        var matchedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var views = new List<ScheduledJobStatusView>(jobs.Count);
        foreach (var job in jobs)
        {
            var key = TaskKey(job.TaskFolder, job.TaskName);
            if (!tasksByKey.TryGetValue(key, out var status))
                status = await taskProvider.GetAsync(job.TaskFolder, job.TaskName, ct);

            if (status is not null)
                matchedKeys.Add(key);

            views.Add(new ScheduledJobStatusView(job, status, status is not null));
        }

        var untracked = narniaTasks
            .Where(t => !matchedKeys.Contains(TaskKey(t.TaskFolder, t.TaskName)))
            .ToList();

        return new ScheduledJobListView(taskProvider.IsSupported, views, untracked);
    }

    /// <inheritdoc />
    public ValueTask<ScheduledJob?> GetAsync(string id, CancellationToken ct = default) =>
        registry.GetByIdAsync(id, ct);

    /// <inheritdoc />
    public async ValueTask<ScheduledJobCreateResult> CreateAsync(
        ScheduledJobInput input, bool register, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(input.Name))
            return ScheduledJobCreateResult.Failure("A job name is required.");
        if (string.IsNullOrWhiteSpace(input.Prompt))
            return ScheduledJobCreateResult.Failure("A prompt is required (it is what Copilot runs).");

        var jobId = Guid.NewGuid().ToString();
        var cadence = BuildCadence(input);
        var copilotCommand = await settingsRepository.GetAsync("copilot_command", ct) ?? DefaultCopilotCommand;
        var (script, launcher, registration) = BuildOwnedJob(jobId, input, cadence, copilotCommand);

        // Copy-paste mode: catalog nothing, just hand back the generated wrapper + registration command.
        if (!register)
            return ScheduledJobCreateResult.CopyPaste(CombineForCopyPaste(script, launcher), ScheduledTaskRegistrationScript.Build(registration));

        if (!registrar.IsSupported)
            return ScheduledJobCreateResult.Failure(
                "Registering tasks is not supported on this platform. Copy the command instead.");

        await workspace.WriteScriptAsync(jobId, script, ct);
        await workspace.WriteLauncherAsync(jobId, launcher, ct);
        var draft = BuildOwnedDraft(input, cadence, workspace.ScriptPath(jobId), workspace.LogDirectory(jobId));

        // Create with the pre-chosen id so the workspace folder, marker, and catalog row all agree.
        var job = await registry.CreateWithIdAsync(jobId, draft, DateTimeOffset.UtcNow, ct);
        var outcome = await registrar.RegisterAsync(registration, ct);
        if (!outcome.Ok)
        {
            await registry.DeleteAsync(job.Id, ct);
            workspace.Delete(job.Id);
            return ScheduledJobCreateResult.Failure($"Task registration failed: {outcome.Error}");
        }

        return ScheduledJobCreateResult.Created(job);
    }

    /// <inheritdoc />
    public async ValueTask<ScheduledJobMutationResult> UpdateAsync(
        string id, ScheduledJobInput input, CancellationToken ct = default)
    {
        var existing = await registry.GetByIdAsync(id, ct);
        if (existing is null)
            return ScheduledJobMutationResult.Missing;
        if (string.IsNullOrWhiteSpace(input.Name))
            return ScheduledJobMutationResult.Failure("A job name is required.");
        if (string.IsNullOrWhiteSpace(input.Prompt))
            return ScheduledJobMutationResult.Failure("A prompt is required.");
        if (!registrar.IsSupported)
            return ScheduledJobMutationResult.Failure("Editing tasks is not supported on this platform.");

        var cadence = BuildCadence(input);
        var copilotCommand = await settingsRepository.GetAsync("copilot_command", ct) ?? DefaultCopilotCommand;
        var (script, launcher, registration) = BuildOwnedJob(id, input, cadence, copilotCommand);

        await workspace.WriteScriptAsync(id, script, ct);
        await workspace.WriteLauncherAsync(id, launcher, ct);
        var draft = BuildOwnedDraft(input, cadence, workspace.ScriptPath(id), workspace.LogDirectory(id));
        await registry.UpdateAsync(id, draft, DateTimeOffset.UtcNow, ct);

        // Register with -Force overwrites the existing task in place (trigger/action refreshed).
        var outcome = await registrar.RegisterAsync(registration, ct);
        if (!outcome.Ok)
            return ScheduledJobMutationResult.Failure($"Task update failed: {outcome.Error}");

        var updated = await registry.GetByIdAsync(id, ct);
        return ScheduledJobMutationResult.Succeeded(updated);
    }

    /// <inheritdoc />
    public async ValueTask<ScheduledJobMutationResult> SetEnabledAsync(
        string id, bool enabled, CancellationToken ct = default)
    {
        var job = await registry.GetByIdAsync(id, ct);
        if (job is null)
            return ScheduledJobMutationResult.Missing;
        var r = await registrar.SetEnabledAsync(job.TaskFolder, job.TaskName, enabled, ct);
        return r.Ok
            ? ScheduledJobMutationResult.Succeeded(job)
            : ScheduledJobMutationResult.Failure(r.Error ?? "Failed to update the task.");
    }

    /// <inheritdoc />
    public async ValueTask<ScheduledJobMutationResult> RunAsync(string id, CancellationToken ct = default)
    {
        var job = await registry.GetByIdAsync(id, ct);
        if (job is null)
            return ScheduledJobMutationResult.Missing;
        var r = await registrar.RunAsync(job.TaskFolder, job.TaskName, ct);
        return r.Ok
            ? ScheduledJobMutationResult.Succeeded(job)
            : ScheduledJobMutationResult.Failure(r.Error ?? "Failed to start the task.");
    }

    /// <inheritdoc />
    public async ValueTask<ScheduledJobMutationResult> DeleteAsync(string id, CancellationToken ct = default)
    {
        var job = await registry.GetByIdAsync(id, ct);
        if (job is null)
            return ScheduledJobMutationResult.Missing;

        // Every job is first-class: it owns its scheduled task and generated script, so deleting a
        // job always removes both alongside the catalog row.
        await registrar.DeleteAsync(job.TaskFolder, job.TaskName, ct);
        workspace.Delete(id);
        await registry.DeleteAsync(id, ct);
        return ScheduledJobMutationResult.Succeeded(job);
    }

    /// <inheritdoc />
    public async ValueTask<ScheduledJobLogView> GetLatestLogAsync(string id, CancellationToken ct = default)
    {
        var job = await registry.GetByIdAsync(id, ct);
        if (job is null)
            return ScheduledJobLogView.Missing;

        // The live state is authoritative while a task runs; LastResult still describes the
        // previous completed run until the scheduler records the new result.
        var status = await taskProvider.GetAsync(job.TaskFolder, job.TaskName, ct);
        var isRunning = status?.State == ScheduledTaskState.Running;

        var path = workspace.LatestLogFile(id);
        if (path is null)
            return ScheduledJobLogView.NoLogYet(isRunning);

        var content = await workspace.ReadLogAsync(path, ct);
        var truncated = content.Length > MaxLogChars;
        return ScheduledJobLogView.Of(path, truncated ? content[^MaxLogChars..] : content, truncated, isRunning);
    }

    private static ScheduleCadence BuildCadence(ScheduledJobInput input)
    {
        var time = TimeOnly.TryParse(input.Time, out var t) ? t : new TimeOnly(5, 0);
        var kind = input.CadenceKind?.ToLowerInvariant() switch
        {
            "weekly" => ScheduleCadenceKind.Weekly,
            "monthly" => ScheduleCadenceKind.Monthly,
            _ => ScheduleCadenceKind.Daily,
        };
        var days = (input.Days ?? [])
            .Select(d => Enum.TryParse<DayOfWeek>(d, ignoreCase: true, out var dow) ? dow : (DayOfWeek?)null)
            .Where(d => d is not null).Select(d => d!.Value).ToList();
        var dayOfMonth = input.DayOfMonth is >= 1 and <= 31 ? input.DayOfMonth.Value : 1;
        return new ScheduleCadence(kind, time, days, dayOfMonth);
    }

    // Builds the generated wrapper script, the hidden-launcher shim, and the standardized task
    // registration for a Narnia-owned job. The task's action is wscript.exe running the launcher
    // (never a bare visible console), which in turn runs the wrapper script under the best
    // available PowerShell host.
    private (string Script, string Launcher, ScheduledTaskRegistration Registration) BuildOwnedJob(
        string jobId, ScheduledJobInput input, ScheduleCadence cadence, string copilotCommand)
    {
        var taskName = string.IsNullOrWhiteSpace(input.TaskName) ? input.Name : input.TaskName!;
        var logDir = workspace.LogDirectory(jobId);
        var script = ScheduledJobScript.Build(
            input.Name, input.Prompt ?? "", input.Cwd, input.AllowFlags, input.CopilotArgs, logDir, copilotCommand);
        var scriptPath = workspace.ScriptPath(jobId);
        var launcher = ScheduledJobLauncherScript.Build(hostResolver.ResolveExecutable(), scriptPath);
        var launcherPath = workspace.LauncherPath(jobId);
        var registration = new ScheduledTaskRegistration(
            jobId, NarniaFolder, taskName, "wscript.exe", $"\"{launcherPath}\"", input.Cwd, cadence);
        return (script, launcher, registration);
    }

    // Copy-paste mode has no workspace to write the launcher to, so both generated files are
    // handed back together with a clear separator telling the user where each one belongs.
    private static string CombineForCopyPaste(string script, string launcher) =>
        $"{script}\n\n' ---- Save the above as run.ps1 and the below as run.vbs (the registration " +
        "command below launches run.vbs, which runs run.ps1 completely hidden) ----\n\n" + launcher;

    // Builds the catalog draft for a Narnia-owned job, keyed to its generated script + log paths.
    private static ScheduledJobDraft BuildOwnedDraft(
        ScheduledJobInput input, ScheduleCadence cadence, string scriptPath, string logDir)
    {
        var taskName = string.IsNullOrWhiteSpace(input.TaskName) ? input.Name : input.TaskName!;
        var skills = (input.Skills ?? [])
            .Where(s => !string.IsNullOrWhiteSpace(s.Skill))
            .Select((s, i) => new ScheduledJobSkill(
                s.Skill,
                Enum.TryParse<SkillResolution>(s.Resolution, ignoreCase: true, out var r) ? r : SkillResolution.Unknown,
                i))
            .ToList();

        // Weekly stores its day names in cadence_days; monthly reuses the same column for its day
        // number, so both round-trip for edit prefill without a schema change.
        var cadenceDays = cadence.Kind switch
        {
            ScheduleCadenceKind.Weekly => string.Join(",", cadence.DaysOfWeek.Select(d => d.ToString())),
            ScheduleCadenceKind.Monthly => cadence.DayOfMonth.ToString(),
            _ => "",
        };

        return new ScheduledJobDraft(
            Name: input.Name, Description: input.Description, Cwd: input.Cwd, Cadence: cadence.Describe(),
            Args: null, ScriptPath: scriptPath, LogDir: logDir, AllowFlags: input.AllowFlags,
            TaskFolder: NarniaFolder, TaskName: taskName, Notes: null, Skills: skills,
            Prompt: input.Prompt, CadenceKind: cadence.Kind.ToString(),
            CadenceTime: cadence.TimeOfDay.ToString("HH\\:mm"), CadenceDays: cadenceDays.Length > 0 ? cadenceDays : null,
            CopilotArgs: input.CopilotArgs);
    }

    private static string TaskKey(string folder, string name) => $"{folder.Trim('\\')}|{name}";
}
