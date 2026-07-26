using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using NexusLabs.Narnia.Core.Models;
using NexusLabs.Narnia.Core.Services;

namespace NexusLabs.Narnia.Web.Mcp;

/// <summary>MCP tools for file-based scheduled-job export, preview, and import.</summary>
[McpServerToolType]
internal sealed class SchedulePackageTools(IScheduledJobPackageService packageService)
{
    [McpServerTool(Name = "export_schedule_package")]
    [Description("Exports selected Narnia scheduled jobs as one versioned JSON package. Use profile 'transfer' to retain non-secret source path hints for another machine you control, or 'share' to remove source-local hints for another user. Generated wrappers, logs, task XML, databases, and secrets are never included.")]
    public async Task<string> ExportSchedulePackageAsync(
        [Description("Narnia scheduled-job ids to export.")] string[] jobIds,
        [Description("'transfer' or 'share'.")] string profile,
        CancellationToken cancellationToken)
    {
        if (jobIds is null)
            return "Error: jobIds is required.";
        var parsedProfile = ParseProfile(profile);
        if (parsedProfile is null)
            return "Error: profile must be 'transfer' or 'share'.";

        var result = await packageService.ExportAsync(
            new ScheduledJobPackageExportRequest(jobIds, parsedProfile.Value),
            cancellationToken);
        return Serialize(result);
    }

    [McpServerTool(Name = "build_schedule_package")]
    [Description("Builds a versioned schedule package from canonical job definitions reconstructed from selected non-Narnia tasks. This does not register or modify any scheduled task.")]
    public async Task<string> BuildSchedulePackageAsync(
        [Description("Portable job definitions to package.")] SchedulePackageJobMcpInput[] jobs,
        [Description("Configuration or external-state requirements identified while inspecting the source task; pass an empty array when none are known.")] SchedulePackageDependencyMcpInput[] dependencies,
        [Description("'transfer' or 'share'.")] string profile,
        CancellationToken cancellationToken)
    {
        if (jobs is null || dependencies is null)
            return "Error: jobs and dependencies arrays are required.";
        var parsedProfile = ParseProfile(profile);
        if (parsedProfile is null)
            return "Error: profile must be 'transfer' or 'share'.";

        var definitions = new List<ScheduledJobDefinition>(jobs.Length);
        foreach (var job in jobs)
        {
            var normalized = ScheduledJobDefinitions.FromInput(job.ToInput());
            if (normalized.Error is not null)
                return $"Error: {normalized.Error}";
            definitions.Add(normalized.Definition!);
        }
        var additionalDependencies = new List<ScheduledJobPackageDependency>(dependencies.Length);
        foreach (var dependency in dependencies)
        {
            if (!dependency.TryToModel(out var model))
                return $"Error: dependency '{dependency.Id}' kind must be 'configuration' or 'externalState'.";
            additionalDependencies.Add(model!);
        }

        var result = await packageService.BuildAsync(
            new ScheduledJobPackageBuildRequest(
                definitions,
                additionalDependencies,
                parsedProfile.Value),
            cancellationToken);
        return Serialize(result);
    }

    [McpServerTool(Name = "preview_schedule_package")]
    [Description("Inspects a schedule package against this computer without changing Narnia or Task Scheduler. Returns required path bindings, task-name conflicts, prior imports, timezone warnings, dependency findings, rendered prompts, and a preview fingerprint required by import_schedule_package.")]
    public async Task<string> PreviewSchedulePackageAsync(
        [Description("Complete .narnia-schedules.json content.")] string packageJson,
        [Description("Destination values for package bindings; pass an empty array when none are required.")] SchedulePackageBindingMcpInput[] bindings,
        [Description("Per-job task-name overrides and duplicate-import decisions; pass an empty array to retain package defaults.")] SchedulePackageJobOptionsMcpInput[] jobs,
        CancellationToken cancellationToken)
    {
        if (bindings is null || jobs is null)
            return "Error: bindings and jobs arrays are required.";
        var result = await packageService.PreviewAsync(
            new ScheduledJobPackagePreviewRequest(
                packageJson,
                bindings.Select(binding => binding.ToModel()).ToArray(),
                jobs.Select(job => job.ToModel()).ToArray()),
            cancellationToken);
        return Serialize(result);
    }

    [McpServerTool(Name = "import_schedule_package")]
    [Description("Imports a package only after a current successful preview. Every destination job receives a new local id and is registered disabled; this tool never enables, runs, auto-installs dependencies, clones repositories, or disables source tasks.")]
    public async Task<string> ImportSchedulePackageAsync(
        [Description("Complete .narnia-schedules.json content.")] string packageJson,
        [Description("Preview fingerprint returned by preview_schedule_package.")] string previewFingerprint,
        [Description("Destination values for package bindings; pass an empty array when none are required.")] SchedulePackageBindingMcpInput[] bindings,
        [Description("Per-job task-name overrides and duplicate-import decisions, identical to preview; pass an empty array to retain package defaults.")] SchedulePackageJobOptionsMcpInput[] jobs,
        CancellationToken cancellationToken)
    {
        if (bindings is null || jobs is null)
            return "Error: bindings and jobs arrays are required.";
        var result = await packageService.ImportAsync(
            new ScheduledJobPackageImportRequest(
                packageJson,
                bindings.Select(binding => binding.ToModel()).ToArray(),
                jobs.Select(job => job.ToModel()).ToArray(),
                previewFingerprint),
            cancellationToken);
        return Serialize(result);
    }

    private static ScheduledJobPackageProfile? ParseProfile(string profile) =>
        Enum.TryParse<ScheduledJobPackageProfile>(profile, ignoreCase: true, out var parsed)
            ? parsed
            : null;

    private static string Serialize(ScheduledJobPackageExportResult result) =>
        JsonSerializer.Serialize(result, SchedulePackageWebJsonContext.Default.ScheduledJobPackageExportResult);

    private static string Serialize(ScheduledJobPackagePreviewResult result) =>
        JsonSerializer.Serialize(result, SchedulePackageWebJsonContext.Default.ScheduledJobPackagePreviewResult);

    private static string Serialize(ScheduledJobPackageImportResult result) =>
        JsonSerializer.Serialize(result, SchedulePackageWebJsonContext.Default.ScheduledJobPackageImportResult);
}

/// <summary>A job definition supplied to <c>build_schedule_package</c>.</summary>
internal sealed record SchedulePackageJobMcpInput(
    [property: Description("Display name.")] string Name,
    [property: Description("Complete prompt passed to copilot -p.")] string Prompt,
    [property: Description("Working directory, when required.")] string? Cwd,
    [property: Description("Short description.")] string? Description,
    [property: Description("'daily', 'weekly', or 'monthly'.")] string CadenceKind,
    [property: Description("Local HH:mm fire time.")] string Time,
    [property: Description("Weekly day names.")] string[]? Days,
    [property: Description("Monthly day number.")] int? DayOfMonth,
    [property: Description("Copilot allow flags.")] string? AllowFlags,
    [property: Description("Additional Copilot arguments.")] string? CopilotArgs,
    [property: Description("Preferred destination Task Scheduler name.")] string? TaskName,
    [property: Description("Ordered skill metadata.")] ScheduleSkillMcpInput[]? Skills)
{
    public ScheduledJobInput ToInput() =>
        new(
            Name,
            Description,
            Cwd,
            Prompt,
            AllowFlags,
            CopilotArgs,
            TaskName,
            CadenceKind,
            Time,
            Days,
            DayOfMonth,
            Skills?.Select(skill => new ScheduledJobSkillInput(skill.Skill, skill.Resolution)).ToArray());
}

/// <summary>A destination package-binding value supplied through MCP.</summary>
internal sealed record SchedulePackageBindingMcpInput(
    [property: Description("Package binding id.")] string Id,
    [property: Description("Destination value.")] string Value)
{
    public ScheduledJobPackageBindingValue ToModel() => new(Id, Value);
}

/// <summary>A configuration or external-state requirement supplied while building a package.</summary>
internal sealed record SchedulePackageDependencyMcpInput(
    [property: Description("Stable lowercase dependency id.")] string Id,
    [property: Description("'configuration' or 'externalState'.")] string Kind,
    [property: Description("Human-readable requirement name.")] string Name,
    [property: Description("Whether the requirement is necessary for correct execution.")] bool Required,
    [property: Description("Related package binding id, when one already exists.")] string? BindingId,
    [property: Description("Path relative to the related binding, when applicable.")] string? RelativePath,
    [property: Description("Setup or state-migration guidance.")] string? Description)
{
    public bool TryToModel(out ScheduledJobPackageDependency? dependency)
    {
        ScheduledJobPackageDependencyKind kind;
        if (string.Equals(Kind, "externalState", StringComparison.OrdinalIgnoreCase))
            kind = ScheduledJobPackageDependencyKind.ExternalState;
        else if (string.Equals(Kind, "configuration", StringComparison.OrdinalIgnoreCase))
            kind = ScheduledJobPackageDependencyKind.Configuration;
        else
        {
            dependency = null;
            return false;
        }

        dependency = new ScheduledJobPackageDependency(
            Id,
            kind,
            Name,
            Required,
            null,
            null,
            null,
            BindingId,
            RelativePath,
            Description);
        return true;
    }
}

/// <summary>Per-job destination choices supplied through MCP.</summary>
internal sealed record SchedulePackageJobOptionsMcpInput(
    [property: Description("Portable job id.")] string PortableJobId,
    [property: Description("Destination task-name override, or null to keep the package name.")] string? TaskName,
    [property: Description("True only when intentionally importing an additional copy of a prior import.")] bool AllowDuplicate,
    [property: Description("True to omit this job from the current import batch.")] bool Skip)
{
    public ScheduledJobPackageJobOptions ToModel() =>
        new(PortableJobId, TaskName, AllowDuplicate, Skip);
}
