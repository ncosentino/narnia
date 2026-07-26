using NexusLabs.Narnia.Core.Models;
using NexusLabs.Narnia.Core.Repositories;

namespace NexusLabs.Narnia.Core.Services;

internal sealed class ScheduledJobPackagePreviewer(
    IScheduledJobService jobService,
    IScheduledJobImportRepository importRepository,
    ScheduledJobPackageDefinitionRenderer definitionRenderer,
    ScheduledJobPackageDependencyInspector dependencyInspector)
{
    public async ValueTask<ScheduledJobPackagePreviewResult> PreviewAsync(
        ScheduledJobPackagePreviewRequest request,
        CancellationToken ct)
    {
        var parsed = ScheduledJobPackageFormat.ParseAndValidate(request.PackageJson);
        if (parsed.Error is not null)
            return Failure(parsed.Error);

        var package = parsed.Package!;
        var bindingValuesResult = NormalizeBindingValues(package, request.Bindings);
        if (bindingValuesResult.Error is not null)
            return Failure(bindingValuesResult.Error);
        var optionsResult = NormalizeJobOptions(package, request.JobOptions);
        if (optionsResult.Error is not null)
            return Failure(optionsResult.Error);

        var bindingPreviews = definitionRenderer.ResolveBindings(package, bindingValuesResult.Values!);
        var resolvedBindings = bindingPreviews
            .Where(binding => binding.Resolved && binding.ResolvedValue is not null)
            .ToDictionary(binding => binding.Id, binding => binding.ResolvedValue!, StringComparer.Ordinal);

        var live = await jobService.ListAsync(ct);
        var occupiedTaskNames = live.Jobs
            .Select(view => view.Job.TaskName)
            .Concat(live.Untracked.Select(task => task.TaskName))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var packageTaskNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var installedSkills = dependencyInspector.DiscoverInstalledPluginSkills();
        var destinationTimeZone = TimeZoneInfo.Local.Id;
        var jobPreviews = new List<ScheduledJobPackageJobPreview>(package.Jobs.Count);

        foreach (var packagedJob in package.Jobs)
        {
            var jobOptions = optionsResult.Values![packagedJob.PortableJobId];
            var targetTaskName = string.IsNullOrWhiteSpace(jobOptions.TaskName)
                ? packagedJob.Definition.TaskName
                : jobOptions.TaskName.Trim();
            var findings = new List<ScheduledJobPackageFinding>();

            if (jobOptions.Skip)
            {
                findings.Add(new ScheduledJobPackageFinding(
                    "skipped",
                    ScheduledJobPackageFindingSeverity.Info,
                    "This job is excluded from the current import batch.",
                    null));
                jobPreviews.Add(new ScheduledJobPackageJobPreview(
                    packagedJob.PortableJobId,
                    packagedJob.Definition.Name,
                    targetTaskName,
                    ScheduledJobPackagePreviewStatus.Ready,
                    true,
                    false,
                    null,
                    findings));
                continue;
            }

            if (!jobService.RegistrarSupported)
            {
                findings.Add(Error(
                    "scheduler-unsupported",
                    "The destination platform cannot register scheduled tasks."));
            }
            if (!ScheduledJobPackageText.IsValidTaskName(targetTaskName))
            {
                findings.Add(Error(
                    "invalid-task-name",
                    "The destination task name is empty, too long, or contains a path separator/control character."));
            }

            foreach (var bindingId in ScheduledJobPackageText.RequiredBindingIds(packagedJob.Definition))
            {
                if (!resolvedBindings.ContainsKey(bindingId))
                {
                    findings.Add(Error(
                        "binding-required",
                        $"Required binding '{bindingId}' is not resolved.",
                        bindingId));
                }
            }

            var rendered = ScheduledJobPackageDefinitionRenderer.RenderDefinition(
                packagedJob.Definition,
                targetTaskName,
                resolvedBindings);
            if (rendered.Error is not null)
                findings.Add(Error("render-failed", rendered.Error));

            if (occupiedTaskNames.Contains(targetTaskName) || !packageTaskNames.Add(targetTaskName))
            {
                findings.Add(Error(
                    "task-name-conflict",
                    $"Task name '{targetTaskName}' is already in use on the destination."));
            }

            var activeImports = await importRepository.GetActiveAsync(
                package.PackageId,
                packagedJob.PortableJobId,
                ct);
            if (activeImports.Count > 0 && !jobOptions.AllowDuplicate)
            {
                findings.Add(Error(
                    "already-imported",
                    $"This portable job is already imported as {string.Join(", ", activeImports.Select(record => record.JobId))}."));
            }
            else if (activeImports.Count > 0)
            {
                findings.Add(Warning(
                    "duplicate-import",
                    "Another copy of this portable job already exists; this import will create an additional disabled job."));
            }

            if (!string.Equals(package.Source.TimeZoneId, destinationTimeZone, StringComparison.Ordinal))
            {
                findings.Add(Warning(
                    "timezone-review",
                    $"The package was created in '{package.Source.TimeZoneId}', while this machine uses '{destinationTimeZone}'. The local wall-clock time is preserved."));
            }

            foreach (var skill in packagedJob.Definition.Skills.OrderBy(skill => skill.Order))
            {
                dependencyInspector.InspectSkillDependency(
                    skill,
                    rendered.Definition?.WorkingDirectory,
                    installedSkills,
                    findings);
            }

            foreach (var dependency in package.Dependencies.Where(dependency =>
                         dependency.Kind is ScheduledJobPackageDependencyKind.Configuration or
                             ScheduledJobPackageDependencyKind.ExternalState))
            {
                var code = dependency.Kind == ScheduledJobPackageDependencyKind.ExternalState
                    ? "external-state-omitted"
                    : "configuration-required";
                var message = dependency.Description ??
                    $"{dependency.Name} must be configured separately on the destination.";
                findings.Add(new ScheduledJobPackageFinding(
                    code,
                    dependency.Required
                        ? ScheduledJobPackageFindingSeverity.Warning
                        : ScheduledJobPackageFindingSeverity.Info,
                    message,
                    dependency.BindingId));
            }

            if (rendered.Definition is not null)
            {
                ScheduledJobPackageDefinitionRenderer.InspectRenderedDefinition(
                    rendered.Definition,
                    resolvedBindings.Values,
                    findings);
            }

            var status = Classify(findings);
            jobPreviews.Add(new ScheduledJobPackageJobPreview(
                packagedJob.PortableJobId,
                packagedJob.Definition.Name,
                targetTaskName,
                status,
                findings.All(finding => finding.Severity != ScheduledJobPackageFindingSeverity.Error),
                true,
                rendered.Definition,
                findings));
        }

        var packageFingerprint = ScheduledJobPackageFormat.PackageFingerprint(request.PackageJson);
        var previewFingerprint = ScheduledJobPackageFormat.PreviewFingerprint(
            packageFingerprint,
            destinationTimeZone,
            bindingPreviews,
            jobPreviews,
            optionsResult.Values!);
        return new ScheduledJobPackagePreviewResult(
            true,
            null,
            package.PackageId,
            packageFingerprint,
            previewFingerprint,
            bindingPreviews,
            jobPreviews);
    }

    private static ScheduledJobPackageNormalizedValues NormalizeBindingValues(
        ScheduledJobPackage package,
        IReadOnlyList<ScheduledJobPackageBindingValue> supplied)
    {
        if (supplied.GroupBy(value => value.Id, StringComparer.Ordinal).Any(group => group.Count() > 1))
            return new(null, "Each binding may be supplied at most once.");

        var known = package.Bindings.Select(binding => binding.Id).ToHashSet(StringComparer.Ordinal);
        var unknown = supplied.FirstOrDefault(value => !known.Contains(value.Id));
        if (unknown is not null)
            return new(null, $"Unknown binding '{unknown.Id}'.");

        return new(
            supplied.ToDictionary(
                value => value.Id,
                value => value.Value,
                StringComparer.Ordinal),
            null);
    }

    private static ScheduledJobPackageNormalizedOptions NormalizeJobOptions(
        ScheduledJobPackage package,
        IReadOnlyList<ScheduledJobPackageJobOptions> supplied)
    {
        if (supplied.GroupBy(value => value.PortableJobId, StringComparer.Ordinal).Any(group => group.Count() > 1))
            return new(null, "Each portable job may have at most one options entry.");

        var known = package.Jobs.Select(job => job.PortableJobId).ToHashSet(StringComparer.Ordinal);
        var unknown = supplied.FirstOrDefault(value => !known.Contains(value.PortableJobId));
        if (unknown is not null)
            return new(null, $"Unknown portable job '{unknown.PortableJobId}'.");

        var values = new Dictionary<string, ScheduledJobPackageJobOptions>(StringComparer.Ordinal);
        foreach (var job in package.Jobs)
        {
            values[job.PortableJobId] = supplied.FirstOrDefault(
                option => string.Equals(
                    option.PortableJobId,
                    job.PortableJobId,
                    StringComparison.Ordinal))
                ?? new ScheduledJobPackageJobOptions(job.PortableJobId, null, false, false);
        }

        return new(values, null);
    }

    private static ScheduledJobPackagePreviewStatus Classify(
        IReadOnlyCollection<ScheduledJobPackageFinding> findings)
    {
        if (findings.Any(finding => finding.Code == "binding-required"))
            return ScheduledJobPackagePreviewStatus.NeedsBinding;
        if (findings.Any(finding => finding.Code == "task-name-conflict"))
            return ScheduledJobPackagePreviewStatus.TaskNameConflict;
        if (findings.Any(finding => finding.Code == "already-imported"))
            return ScheduledJobPackagePreviewStatus.AlreadyImported;
        if (findings.Any(finding => finding.Severity == ScheduledJobPackageFindingSeverity.Error))
            return ScheduledJobPackagePreviewStatus.Invalid;
        if (findings.Any(finding =>
                finding.Code is "plugin-skill-missing" or "repo-skill-missing" or "skill-resolution-unknown"))
        {
            return ScheduledJobPackagePreviewStatus.MissingDependency;
        }

        return ScheduledJobPackagePreviewStatus.Ready;
    }

    private static ScheduledJobPackageFinding Error(
        string code,
        string message,
        string? bindingId = null) =>
        new(code, ScheduledJobPackageFindingSeverity.Error, message, bindingId);

    private static ScheduledJobPackageFinding Warning(
        string code,
        string message) =>
        new(code, ScheduledJobPackageFindingSeverity.Warning, message, null);

    private static ScheduledJobPackagePreviewResult Failure(string error) =>
        new(false, error, null, null, null, [], []);
}

internal sealed record ScheduledJobPackageNormalizedValues(
    IReadOnlyDictionary<string, string>? Values,
    string? Error);

internal sealed record ScheduledJobPackageNormalizedOptions(
    IReadOnlyDictionary<string, ScheduledJobPackageJobOptions>? Values,
    string? Error);
