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
    INarniaSettingsRepository settingsRepository,
    IScheduledRunOutcomeReader runOutcomeReader) : IScheduledJobService
{
    private const string NarniaFolder = @"\Narnia\";
    private const int MaxLogChars = 100_000;
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

            // Only a run the scheduler already called successful can be hiding an interrupted
            // session; every other classification is surfaced as-is, so there is nothing to
            // recover and no reason to pay for the file reads.
            var lastRun = status.GetHealthKind() == ScheduledTaskHealthKind.Succeeded
                ? await runOutcomeReader.ReadLatestAsync(job.Id, ct)
                : null;

            views.Add(new ScheduledJobStatusView(job, status, status is not null, lastRun));
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
        => await CreateAsync(input, register, enabled: true, ct);

    /// <inheritdoc />
    public async ValueTask<ScheduledJobCreateResult> CreateDisabledAsync(
        ScheduledJobInput input,
        CancellationToken ct) =>
        await CreateAsync(input, register: true, enabled: false, ct);

    private async ValueTask<ScheduledJobCreateResult> CreateAsync(
        ScheduledJobInput input,
        bool register,
        bool enabled,
        CancellationToken ct)
    {
        var definitionResult = ScheduledJobDefinitions.FromInput(input);
        if (definitionResult.Error is not null)
            return ScheduledJobCreateResult.Failure(definitionResult.Error);
        var definition = definitionResult.Definition!;

        var jobId = Guid.NewGuid().ToString();
        var copilotCommand =
            await settingsRepository.GetAsync(CopilotSettingKeys.Command, ct) ??
            CopilotSettingKeys.DefaultCommand;
        var (script, launcher, registration) = BuildOwnedJob(jobId, definition, copilotCommand, enabled);

        // Copy-paste mode: catalog nothing, just hand back the generated wrapper + registration command.
        if (!register)
            return ScheduledJobCreateResult.CopyPaste(CombineForCopyPaste(script, launcher), ScheduledTaskRegistrationScript.Build(registration));

        if (!registrar.IsSupported)
            return ScheduledJobCreateResult.Failure(
                "Registering tasks is not supported on this platform. Copy the command instead.");

        await workspace.WriteScriptAsync(jobId, script, ct);
        await workspace.WriteLauncherAsync(jobId, launcher, ct);
        var draft = BuildOwnedDraft(definition, workspace.ScriptPath(jobId), workspace.LogDirectory(jobId));

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
        var definitionResult = ScheduledJobDefinitions.FromInput(input);
        if (definitionResult.Error is not null)
            return ScheduledJobMutationResult.Failure(definitionResult.Error);
        var definition = definitionResult.Definition!;
        if (!registrar.IsSupported)
            return ScheduledJobMutationResult.Failure("Editing tasks is not supported on this platform.");

        var currentStatus = await taskProvider.GetAsync(existing.TaskFolder, existing.TaskName, ct);
        var enabled = currentStatus?.State != ScheduledTaskState.Disabled;
        var copilotCommand =
            await settingsRepository.GetAsync(CopilotSettingKeys.Command, ct) ??
            CopilotSettingKeys.DefaultCommand;
        var (script, launcher, registration) = BuildOwnedJob(id, definition, copilotCommand, enabled);

        await workspace.WriteScriptAsync(id, script, ct);
        await workspace.WriteLauncherAsync(id, launcher, ct);
        var draft = BuildOwnedDraft(definition, workspace.ScriptPath(id), workspace.LogDirectory(id));
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

        // Do not orphan a live task by deleting its catalog/workspace after a scheduler failure.
        if (registrar.IsSupported)
        {
            var taskDeletion = await registrar.DeleteAsync(job.TaskFolder, job.TaskName, ct);
            if (!taskDeletion.Ok)
            {
                return ScheduledJobMutationResult.Failure(
                    taskDeletion.Error ?? "Failed to remove the scheduled task.");
            }
        }

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

    // Builds the generated wrapper script, the hidden-launcher shim, and the standardized task
    // registration for a Narnia-owned job. The task's action is wscript.exe running the launcher
    // (never a bare visible console), which in turn runs the wrapper script under the best
    // available PowerShell host.
    private (string Script, string Launcher, ScheduledTaskRegistration Registration) BuildOwnedJob(
        string jobId,
        ScheduledJobDefinition definition,
        string copilotCommand,
        bool enabled)
    {
        var logDir = workspace.LogDirectory(jobId);
        var script = ScheduledJobScript.Build(
            definition.Name,
            definition.Prompt,
            definition.WorkingDirectory,
            definition.AllowFlags,
            definition.CopilotArgs,
            logDir,
            copilotCommand);
        var scriptPath = workspace.ScriptPath(jobId);
        var launcher = ScheduledJobLauncherScript.Build(hostResolver.ResolveExecutable(), scriptPath);
        var launcherPath = workspace.LauncherPath(jobId);
        var registration = new ScheduledTaskRegistration(
            jobId,
            NarniaFolder,
            definition.TaskName,
            "wscript.exe",
            $"\"{launcherPath}\"",
            definition.WorkingDirectory,
            definition.Cadence,
            enabled);
        return (script, launcher, registration);
    }

    // Copy-paste mode has no workspace to write the launcher to, so both generated files are
    // handed back together with a clear separator telling the user where each one belongs.
    private static string CombineForCopyPaste(string script, string launcher) =>
        $"{script}\n\n' ---- Save the above as run.ps1 and the below as run.vbs (the registration " +
        "command below launches run.vbs, which runs run.ps1 completely hidden) ----\n\n" + launcher;

    // Builds the catalog draft for a Narnia-owned job, keyed to its generated script + log paths.
    private static ScheduledJobDraft BuildOwnedDraft(
        ScheduledJobDefinition definition,
        string scriptPath,
        string logDir)
    {
        // Weekly stores its day names in cadence_days; monthly reuses the same column for its day
        // number, so both round-trip for edit prefill without a schema change.
        var cadenceDays = definition.Cadence.Kind switch
        {
            ScheduleCadenceKind.Weekly => string.Join(",", definition.Cadence.DaysOfWeek.Select(d => d.ToString())),
            ScheduleCadenceKind.Monthly => definition.Cadence.DayOfMonth.ToString(),
            _ => "",
        };

        return new ScheduledJobDraft(
            Name: definition.Name,
            Description: definition.Description,
            Cwd: definition.WorkingDirectory,
            Cadence: definition.Cadence.Describe(),
            Args: null,
            ScriptPath: scriptPath,
            LogDir: logDir,
            AllowFlags: definition.AllowFlags,
            TaskFolder: NarniaFolder,
            TaskName: definition.TaskName,
            Notes: null,
            Skills: definition.Skills,
            Prompt: definition.Prompt,
            CadenceKind: definition.Cadence.Kind.ToString(),
            CadenceTime: definition.Cadence.TimeOfDay.ToString("HH\\:mm"),
            CadenceDays: cadenceDays.Length > 0 ? cadenceDays : null,
            CopilotArgs: definition.CopilotArgs);
    }

    private static string TaskKey(string folder, string name) => $"{folder.Trim('\\')}|{name}";
}
