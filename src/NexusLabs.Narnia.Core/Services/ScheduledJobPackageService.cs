using System.IO.Abstractions;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using NexusLabs.Narnia.Core.Configuration;
using NexusLabs.Narnia.Core.Models;
using NexusLabs.Narnia.Core.Repositories;

namespace NexusLabs.Narnia.Core.Services;

/// <summary>Default implementation of <see cref="IScheduledJobPackageService"/>.</summary>
public sealed partial class ScheduledJobPackageService(
    IScheduledJobService jobService,
    IScheduledJobImportRepository importRepository,
    NarniaOptions options,
    IFileSystem fileSystem) : IScheduledJobPackageService
{
    /// <summary>The fixed identifier written into every supported package.</summary>
    public const string PackageFormat = "narnia.schedule-package";

    /// <summary>The package schema version supported by this build.</summary>
    public const int CurrentSchemaVersion = 1;

    private const int MaxPackageChars = 5_000_000;
    private const int MaxJobs = 100;
    private const int MaxPromptChars = 1_000_000;
    private const string BindingTokenPrefix = "{{narnia:";
    private const string BindingTokenSuffix = "}}";

    /// <inheritdoc />
    public async ValueTask<ScheduledJobPackageExportResult> ExportAsync(
        ScheduledJobPackageExportRequest request,
        CancellationToken ct)
    {
        var ids = request.JobIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (ids.Length == 0)
            return ExportFailure("Select at least one scheduled job to export.");
        if (ids.Length > MaxJobs)
            return ExportFailure($"A package may contain at most {MaxJobs} jobs.");

        var list = await jobService.ListAsync(ct);
        var jobsById = list.Jobs.ToDictionary(view => view.Job.Id, StringComparer.Ordinal);
        var sources = new List<PackageSourceJob>(ids.Length);
        foreach (var id in ids)
        {
            if (!jobsById.TryGetValue(id, out var view))
                return ExportFailure($"Scheduled job '{id}' was not found.");

            var definition = ScheduledJobDefinitions.FromJob(view.Job);
            if (definition.Error is not null)
                return ExportFailure(definition.Error);

            sources.Add(new PackageSourceJob(
                definition.Definition!,
                view.Job.Id,
                view.Job.TaskName,
                view.Status is null ? null : view.Status.State != ScheduledTaskState.Disabled,
                view.Job.Id));
        }

        var packageId = StablePackageId(ids, request.Profile);
        return BuildPackage(sources, request.Profile, packageId, []);
    }

    /// <inheritdoc />
    public ValueTask<ScheduledJobPackageExportResult> BuildAsync(
        ScheduledJobPackageBuildRequest request,
        CancellationToken ct)
    {
        _ = ct;
        if (request.Definitions.Count == 0)
            return ValueTask.FromResult(ExportFailure("Provide at least one scheduled job definition."));
        if (request.Definitions.Count > MaxJobs)
            return ValueTask.FromResult(ExportFailure($"A package may contain at most {MaxJobs} jobs."));

        var sources = request.Definitions
            .Select(definition => new PackageSourceJob(
                definition,
                null,
                null,
                null,
                Guid.NewGuid().ToString()))
            .ToArray();
        return ValueTask.FromResult(BuildPackage(
            sources,
            request.Profile,
            Guid.NewGuid().ToString(),
            request.AdditionalDependencies));
    }

    /// <inheritdoc />
    public async ValueTask<ScheduledJobPackagePreviewResult> PreviewAsync(
        ScheduledJobPackagePreviewRequest request,
        CancellationToken ct)
    {
        var parsed = ParseAndValidate(request.PackageJson);
        if (parsed.Error is not null)
            return PreviewFailure(parsed.Error);

        var package = parsed.Package!;
        var bindingValuesResult = NormalizeBindingValues(package, request.Bindings);
        if (bindingValuesResult.Error is not null)
            return PreviewFailure(bindingValuesResult.Error);
        var optionsResult = NormalizeJobOptions(package, request.JobOptions);
        if (optionsResult.Error is not null)
            return PreviewFailure(optionsResult.Error);

        var bindingPreviews = ResolveBindings(package, bindingValuesResult.Values!);
        var resolvedBindings = bindingPreviews
            .Where(binding => binding.Resolved && binding.ResolvedValue is not null)
            .ToDictionary(binding => binding.Id, binding => binding.ResolvedValue!, StringComparer.Ordinal);

        var live = await jobService.ListAsync(ct);
        var occupiedTaskNames = live.Jobs
            .Select(view => view.Job.TaskName)
            .Concat(live.Untracked.Select(task => task.TaskName))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var packageTaskNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var installedSkills = DiscoverInstalledPluginSkills();
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
            if (!IsValidTaskName(targetTaskName))
            {
                findings.Add(Error(
                    "invalid-task-name",
                    "The destination task name is empty, too long, or contains a path separator/control character."));
            }

            foreach (var bindingId in RequiredBindingIds(packagedJob.Definition))
            {
                if (!resolvedBindings.ContainsKey(bindingId))
                {
                    findings.Add(Error(
                        "binding-required",
                        $"Required binding '{bindingId}' is not resolved.",
                        bindingId));
                }
            }

            var rendered = RenderDefinition(packagedJob.Definition, targetTaskName, resolvedBindings);
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
                InspectSkillDependency(
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
                InspectRenderedDefinition(rendered.Definition, resolvedBindings.Values, findings);

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

        var packageFingerprint = Sha256(request.PackageJson);
        var previewFingerprint = PreviewFingerprint(
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

    /// <inheritdoc />
    public async ValueTask<ScheduledJobPackageImportResult> ImportAsync(
        ScheduledJobPackageImportRequest request,
        CancellationToken ct)
    {
        var preview = await PreviewAsync(
            new ScheduledJobPackagePreviewRequest(
                request.PackageJson,
                request.Bindings,
                request.JobOptions),
            ct);
        if (!preview.Ok)
            return ImportFailure(preview.Error ?? "Package preview failed.");
        if (!string.Equals(preview.PreviewFingerprint, request.PreviewFingerprint, StringComparison.Ordinal))
            return ImportFailure("The package preview is stale. Preview the package again before importing.");
        if (preview.Jobs.Any(job => !job.CanImport))
            return ImportFailure("One or more packaged jobs still have blocking preview findings.");
        var selectedJobs = preview.Jobs
            .Where(job => job.WillImport)
            .ToArray();
        if (selectedJobs.Length == 0)
            return ImportFailure("Select at least one packaged job to import.");
        if (selectedJobs.Any(job => job.RenderedDefinition is null))
            return ImportFailure("One or more selected jobs could not be rendered.");

        var parsed = ParseAndValidate(request.PackageJson);
        if (parsed.Error is not null)
            return ImportFailure(parsed.Error);
        var package = parsed.Package!;

        var imported = new List<ScheduledJobPackageImportedJob>();
        var created = new List<(ScheduledJobPackageJob PackageJob, ScheduledJob LocalJob)>();
        foreach (var jobPreview in selectedJobs)
        {
            var packagedJob = package.Jobs.Single(job => job.PortableJobId == jobPreview.PortableJobId);
            var create = await jobService.CreateDisabledAsync(
                ScheduledJobDefinitions.ToInput(jobPreview.RenderedDefinition!),
                ct);
            if (!create.Ok || create.Job is null)
            {
                imported.Add(new ScheduledJobPackageImportedJob(
                    jobPreview.PortableJobId,
                    false,
                    null,
                    jobPreview.TargetTaskName,
                    create.Error ?? "The destination job could not be created."));
                return await RollBackAsync(
                    package,
                    preview.PackageFingerprint!,
                    imported,
                    created,
                    "Package import failed; previously created jobs were rolled back.",
                    ct);
            }

            var localJob = create.Job;
            var record = new ScheduledJobImportRecord(
                localJob.Id,
                package.PackageId,
                packagedJob.PortableJobId,
                packagedJob.DefinitionFingerprint,
                packagedJob.SourceJobId,
                DateTimeOffset.UtcNow);
            try
            {
                await importRepository.AddAsync(record, ct);
            }
            catch (Microsoft.Data.Sqlite.SqliteException ex)
            {
                imported.Add(new ScheduledJobPackageImportedJob(
                    packagedJob.PortableJobId,
                    false,
                    localJob.Id,
                    localJob.TaskName,
                    $"Import provenance could not be stored: {ex.Message}"));
                created.Add((packagedJob, localJob));
                return await RollBackAsync(
                    package,
                    preview.PackageFingerprint!,
                    imported,
                    created,
                    "Package import failed while recording provenance.",
                    ct);
            }

            created.Add((packagedJob, localJob));
            imported.Add(new ScheduledJobPackageImportedJob(
                packagedJob.PortableJobId,
                true,
                localJob.Id,
                localJob.TaskName,
                null));
        }

        var receipt = new ScheduledJobPackageImportReceipt(
            package.PackageId,
            preview.PackageFingerprint!,
            DateTimeOffset.UtcNow,
            imported);
        return new ScheduledJobPackageImportResult(true, null, imported, receipt);
    }

    private ScheduledJobPackageExportResult BuildPackage(
        IReadOnlyList<PackageSourceJob> sourceJobs,
        ScheduledJobPackageProfile profile,
        string packageId,
        IReadOnlyList<ScheduledJobPackageDependency> additionalDependencies)
    {
        var warnings = new List<string>();
        var bindings = new List<ScheduledJobPackageBinding>();
        var dependencies = additionalDependencies.ToList();
        if (dependencies.Any(dependency =>
                string.IsNullOrWhiteSpace(dependency.Id) ||
                !BindingIdRegex().IsMatch(dependency.Id) ||
                string.IsNullOrWhiteSpace(dependency.Name)) ||
            dependencies.Select(dependency => dependency.Id)
                .Distinct(StringComparer.Ordinal)
                .Count() != dependencies.Count)
        {
            return ExportFailure("Additional dependency ids and names are required and ids must be unique.");
        }

        var usedDependencyIds = dependencies
            .Select(dependency => dependency.Id)
            .ToHashSet(StringComparer.Ordinal);
        var pathBindings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var skillDependencies = new Dictionary<string, ScheduledJobPackageDependency>(StringComparer.Ordinal);
        var installedSkills = DiscoverInstalledPluginSkills();
        var packagedJobs = new List<ScheduledJobPackageJob>(sourceJobs.Count);

        foreach (var source in sourceJobs)
        {
            if (string.IsNullOrWhiteSpace(source.Definition.Name))
                return ExportFailure("Every packaged job requires a name.");
            if (string.IsNullOrWhiteSpace(source.Definition.Prompt))
                return ExportFailure($"Scheduled job '{source.Definition.Name}' requires a prompt.");
            if (source.Definition.Prompt.Length > MaxPromptChars)
                return ExportFailure($"Scheduled job '{source.Definition.Name}' exceeds the {MaxPromptChars:N0}-character prompt limit.");

            string? workingDirectoryBindingId = null;
            if (!string.IsNullOrWhiteSpace(source.Definition.WorkingDirectory))
            {
                workingDirectoryBindingId = GetOrAddPathBinding(
                    source.Definition.WorkingDirectory,
                    $"{source.Definition.Name} working directory",
                    profile,
                    bindings,
                    pathBindings);
            }

            var promptTemplate = TokenizeText(
                source.Definition.Prompt,
                $"{source.Definition.Name} prompt",
                source.Definition.WorkingDirectory,
                workingDirectoryBindingId,
                profile,
                bindings,
                pathBindings)!;
            var descriptionTemplate = TokenizeText(
                source.Definition.Description,
                $"{source.Definition.Name} description",
                source.Definition.WorkingDirectory,
                workingDirectoryBindingId,
                profile,
                bindings,
                pathBindings);
            var allowFlagsTemplate = TokenizeText(
                source.Definition.AllowFlags,
                $"{source.Definition.Name} allow flags",
                source.Definition.WorkingDirectory,
                workingDirectoryBindingId,
                profile,
                bindings,
                pathBindings);
            var copilotArgsTemplate = TokenizeText(
                source.Definition.CopilotArgs,
                $"{source.Definition.Name} Copilot arguments",
                source.Definition.WorkingDirectory,
                workingDirectoryBindingId,
                profile,
                bindings,
                pathBindings);

            if (SecretLikeValueRegex().IsMatch(string.Join(
                    "\n",
                    source.Definition.Description,
                    source.Definition.Prompt,
                    source.Definition.AllowFlags,
                    source.Definition.CopilotArgs)))
            {
                return ExportFailure(
                    $"Scheduled job '{source.Definition.Name}' contains text that resembles an embedded credential. Move the value into destination-local configuration before packaging the job.");
            }

            var portableSkills = new List<ScheduledJobPackageSkill>();
            foreach (var skill in source.Definition.Skills.OrderBy(skill => skill.Order))
            {
                var dependencyKey = $"{skill.Resolution}:{skill.Skill}";
                if (!skillDependencies.TryGetValue(dependencyKey, out var dependency))
                {
                    dependency = BuildSkillDependency(
                        skill,
                        workingDirectoryBindingId,
                        usedDependencyIds,
                        installedSkills,
                        warnings);
                    skillDependencies[dependencyKey] = dependency;
                    dependencies.Add(dependency);
                }

                portableSkills.Add(new ScheduledJobPackageSkill(
                    skill.Skill,
                    skill.Resolution,
                    dependency.Id,
                    portableSkills.Count));
            }

            var cadence = new ScheduledJobPackageCadence(
                source.Definition.Cadence.Kind,
                source.Definition.Cadence.TimeOfDay.ToString("HH\\:mm"),
                source.Definition.Cadence.DaysOfWeek.Select(day => day.ToString()).ToArray(),
                source.Definition.Cadence.DayOfMonth);
            var portableDefinition = new ScheduledJobPortableDefinition(
                source.Definition.Name,
                descriptionTemplate,
                promptTemplate,
                workingDirectoryBindingId,
                allowFlagsTemplate,
                copilotArgsTemplate,
                source.Definition.TaskName,
                cadence,
                portableSkills);
            var definitionFingerprint = FingerprintDefinition(portableDefinition);
            var portableJobId = profile == ScheduledJobPackageProfile.Transfer
                ? source.PortableJobId
                : $"job-{Sha256(source.PortableJobId)[..24].ToLowerInvariant()}";
            packagedJobs.Add(new ScheduledJobPackageJob(
                portableJobId,
                definitionFingerprint,
                portableDefinition,
                profile == ScheduledJobPackageProfile.Transfer ? source.SourceJobId : null,
                profile == ScheduledJobPackageProfile.Transfer ? source.SourceTaskName : null,
                profile == ScheduledJobPackageProfile.Transfer ? source.SourceEnabled : null));
        }

        var package = new ScheduledJobPackage(
            PackageFormat,
            CurrentSchemaVersion,
            packageId,
            profile,
            DateTimeOffset.UtcNow,
            new ScheduledJobPackageSource(CurrentVersion(), TimeZoneInfo.Local.Id),
            bindings,
            dependencies,
            packagedJobs);
        var json = JsonSerializer.Serialize(
            package,
            ScheduledJobPackageJsonContext.Default.ScheduledJobPackage);
        if (json.Length > MaxPackageChars)
            return ExportFailure($"The generated package exceeds the {MaxPackageChars:N0}-character limit.");
        var validation = ParseAndValidate(json);
        if (validation.Error is not null)
            return ExportFailure($"The generated package is invalid: {validation.Error}");

        return new ScheduledJobPackageExportResult(true, null, package, json, warnings);
    }

    private IReadOnlyList<ScheduledJobPackageBindingPreview> ResolveBindings(
        ScheduledJobPackage package,
        IReadOnlyDictionary<string, string> supplied)
    {
        var result = new List<ScheduledJobPackageBindingPreview>(package.Bindings.Count);
        foreach (var binding in package.Bindings)
        {
            supplied.TryGetValue(binding.Id, out var value);
            if (string.IsNullOrWhiteSpace(value) &&
                package.Profile == ScheduledJobPackageProfile.Transfer &&
                !string.IsNullOrWhiteSpace(binding.SourceHint) &&
                BindingExists(binding.Kind, binding.SourceHint))
            {
                value = binding.SourceHint;
            }

            value = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            var error = ValidateBinding(binding, value);
            result.Add(new ScheduledJobPackageBindingPreview(
                binding.Id,
                binding.Kind,
                binding.Description,
                binding.Required,
                binding.SourceHint,
                value,
                error is null && value is not null,
                error));
        }

        return result;
    }

    private RenderDefinitionResult RenderDefinition(
        ScheduledJobPortableDefinition portable,
        string targetTaskName,
        IReadOnlyDictionary<string, string> bindings)
    {
        var prompt = portable.PromptTemplate;
        foreach (var binding in bindings)
            prompt = prompt.Replace(BindingToken(binding.Key), binding.Value, StringComparison.Ordinal);

        var description = RenderText(portable.Description, bindings);
        var allowFlags = RenderText(portable.AllowFlags, bindings);
        var copilotArgs = RenderText(portable.CopilotArgs, bindings);
        if (new[] { prompt, description, allowFlags, copilotArgs }
            .Any(value => value?.Contains(BindingTokenPrefix, StringComparison.Ordinal) == true))
        {
            return new(null, "The rendered definition still contains unresolved Narnia binding tokens.");
        }

        string? cwd = null;
        if (portable.WorkingDirectoryBindingId is not null)
        {
            if (!bindings.TryGetValue(portable.WorkingDirectoryBindingId, out cwd))
                return new(null, $"Working-directory binding '{portable.WorkingDirectoryBindingId}' is unresolved.");
        }

        if (!TimeOnly.TryParse(portable.Cadence.Time, out var time))
            return new(null, $"Cadence time '{portable.Cadence.Time}' is invalid.");
        var days = portable.Cadence.Days
            .Select(day => Enum.TryParse<DayOfWeek>(day, ignoreCase: true, out var parsed)
                ? parsed
                : (DayOfWeek?)null)
            .Where(day => day is not null)
            .Select(day => day!.Value)
            .ToArray();
        if (portable.Cadence.Kind == ScheduleCadenceKind.Weekly && days.Length == 0)
            return new(null, "A weekly cadence requires at least one valid day.");
        if (portable.Cadence.Kind == ScheduleCadenceKind.Monthly &&
            portable.Cadence.DayOfMonth is < 1 or > 31)
        {
            return new(null, "A monthly cadence requires a day between 1 and 31.");
        }

        var cadence = new ScheduleCadence(
            portable.Cadence.Kind,
            time,
            days,
            portable.Cadence.DayOfMonth);
        var definition = new ScheduledJobDefinition(
            portable.Name,
            description,
            cwd,
            prompt,
            allowFlags,
            copilotArgs,
            targetTaskName,
            cadence,
            portable.Skills
                .OrderBy(skill => skill.Order)
                .Select((skill, order) => new ScheduledJobSkill(
                    skill.Skill,
                    skill.Resolution,
                    order))
                .ToArray());
        return new(definition, null);
    }

    private void InspectSkillDependency(
        ScheduledJobPackageSkill skill,
        string? workingDirectory,
        IReadOnlyDictionary<string, PluginSkillLocation> installedSkills,
        ICollection<ScheduledJobPackageFinding> findings)
    {
        var candidates = SkillNameCandidates(skill.Skill);
        if (skill.Resolution == SkillResolution.Plugin)
        {
            if (!candidates.Any(installedSkills.ContainsKey))
            {
                findings.Add(Warning(
                    "plugin-skill-missing",
                    $"Plugin skill '{skill.Skill}' was not found in the destination Copilot plugin directory."));
            }

            return;
        }

        if (skill.Resolution == SkillResolution.RepoLocal)
        {
            if (workingDirectory is null)
            {
                findings.Add(Error(
                    "repo-skill-without-cwd",
                    $"Repo-local skill '{skill.Skill}' requires a working-directory binding."));
                return;
            }

            var exists = candidates.Any(candidate =>
                RepoLocalSkillPaths(workingDirectory, candidate)
                    .Any(fileSystem.File.Exists));
            if (!exists)
            {
                findings.Add(Warning(
                    "repo-skill-missing",
                    $"Repo-local skill '{skill.Skill}' was not found below '{workingDirectory}'."));
            }

            return;
        }

        findings.Add(Warning(
            "skill-resolution-unknown",
            $"Skill '{skill.Skill}' has unknown resolution and must be checked manually."));
    }

    private static void InspectRenderedDefinition(
        ScheduledJobDefinition definition,
        IEnumerable<string> resolvedBindingValues,
        ICollection<ScheduledJobPackageFinding> findings)
    {
        var allDefinitionText = string.Join(
            "\n",
            definition.Description,
            definition.Prompt,
            definition.AllowFlags,
            definition.CopilotArgs);
        if (SecretLikeValueRegex().IsMatch(allDefinitionText))
        {
            findings.Add(Warning(
                "possible-secret",
                "The rendered definition contains text that resembles an embedded credential. Review it before sharing or importing."));
        }

        var boundPaths = resolvedBindingValues.ToArray();
        var unboundPaths = FindAbsolutePaths(allDefinitionText)
            .Where(path => !boundPaths.Any(bound => IsPathCoveredByBinding(path, bound)))
            .ToArray();
        if (unboundPaths.Length > 0)
        {
            findings.Add(Warning(
                "unbound-absolute-path",
                "The rendered prompt still contains an absolute path. Confirm that it is valid on this machine."));
        }

        if (definition.AllowFlags?.Contains("--allow-all", StringComparison.OrdinalIgnoreCase) == true)
        {
            findings.Add(new ScheduledJobPackageFinding(
                "broad-tool-access",
                ScheduledJobPackageFindingSeverity.Info,
                "The job grants broad Copilot tool/path access.",
                null));
        }
    }

    private async ValueTask<ScheduledJobPackageImportResult> RollBackAsync(
        ScheduledJobPackage package,
        string packageFingerprint,
        IReadOnlyList<ScheduledJobPackageImportedJob> currentResults,
        IReadOnlyList<(ScheduledJobPackageJob PackageJob, ScheduledJob LocalJob)> created,
        string error,
        CancellationToken ct)
    {
        var results = currentResults.ToList();
        var cleanupFailures = new List<string>();
        foreach (var item in created.Reverse())
        {
            var deleted = await jobService.DeleteAsync(item.LocalJob.Id, ct);
            if (!deleted.Ok)
            {
                cleanupFailures.Add($"{item.LocalJob.Id}: {deleted.Error}");
                continue;
            }

            try
            {
                await importRepository.DeleteAsync(item.LocalJob.Id, ct);
            }
            catch (Microsoft.Data.Sqlite.SqliteException ex)
            {
                cleanupFailures.Add($"{item.LocalJob.Id} provenance: {ex.Message}");
            }

            var index = results.FindIndex(result => result.LocalJobId == item.LocalJob.Id);
            if (index >= 0)
            {
                results[index] = results[index] with
                {
                    Ok = false,
                    Error = "Rolled back because another job in the package failed.",
                };
            }
        }

        var fullError = cleanupFailures.Count == 0
            ? error
            : $"{error} Cleanup is required for: {string.Join("; ", cleanupFailures)}";
        _ = package;
        _ = packageFingerprint;
        return new ScheduledJobPackageImportResult(false, fullError, results, null);
    }

    private ParsedPackage ParseAndValidate(string packageJson)
    {
        if (string.IsNullOrWhiteSpace(packageJson))
            return new(null, "Package JSON is required.");
        if (packageJson.Length > MaxPackageChars)
            return new(null, $"Package JSON exceeds the {MaxPackageChars:N0}-character limit.");

        ScheduledJobPackage? package;
        try
        {
            package = JsonSerializer.Deserialize(
                packageJson,
                ScheduledJobPackageJsonContext.Default.ScheduledJobPackage);
        }
        catch (JsonException ex)
        {
            return new(null, $"Package JSON is invalid: {ex.Message}");
        }

        if (package is null)
            return new(null, "Package JSON did not contain a package.");
        if (!string.Equals(package.Format, PackageFormat, StringComparison.Ordinal))
            return new(null, $"Unsupported package format '{package.Format}'.");
        if (package.SchemaVersion != CurrentSchemaVersion)
            return new(null, $"Unsupported package schema version {package.SchemaVersion}.");
        if (!Enum.IsDefined(package.Profile))
            return new(null, "Package profile is invalid.");
        if (string.IsNullOrWhiteSpace(package.PackageId))
            return new(null, "Package id is required.");
        if (package.Source is null ||
            string.IsNullOrWhiteSpace(package.Source.TimeZoneId) ||
            package.Bindings is null ||
            package.Dependencies is null ||
            package.Jobs is null)
        {
            return new(null, "Package source, bindings, dependencies, and jobs are required.");
        }
        if (package.Jobs.Count is < 1 or > MaxJobs)
            return new(null, $"A package must contain between 1 and {MaxJobs} jobs.");
        if (package.Bindings.Any(binding =>
                binding is null ||
                string.IsNullOrWhiteSpace(binding.Id) ||
                !BindingIdRegex().IsMatch(binding.Id)))
        {
            return new(null, "Package binding ids must contain only lowercase letters, numbers, and hyphens.");
        }
        if (package.Bindings.Select(binding => binding.Id).Distinct(StringComparer.Ordinal).Count() != package.Bindings.Count)
            return new(null, "Package binding ids must be unique.");
        if (package.Dependencies.Any(dependency =>
                dependency is null ||
                string.IsNullOrWhiteSpace(dependency.Id) ||
                !BindingIdRegex().IsMatch(dependency.Id) ||
                !Enum.IsDefined(dependency.Kind) ||
                string.IsNullOrWhiteSpace(dependency.Name)))
        {
            return new(null, "Every package dependency requires an id and name.");
        }
        if (package.Dependencies.Select(dependency => dependency.Id).Distinct(StringComparer.Ordinal).Count() != package.Dependencies.Count)
            return new(null, "Package dependency ids must be unique.");
        if (package.Jobs.Select(job => job.PortableJobId).Distinct(StringComparer.Ordinal).Count() != package.Jobs.Count)
            return new(null, "Portable job ids must be unique.");

        var bindingIds = package.Bindings.Select(binding => binding.Id).ToHashSet(StringComparer.Ordinal);
        var dependencyIds = package.Dependencies.Select(dependency => dependency.Id).ToHashSet(StringComparer.Ordinal);
        var dependencyWithUnknownBinding = package.Dependencies
            .FirstOrDefault(dependency =>
                dependency.BindingId is not null &&
                !bindingIds.Contains(dependency.BindingId));
        if (dependencyWithUnknownBinding is not null)
            return new(null, $"Dependency '{dependencyWithUnknownBinding.Id}' references an unknown binding.");
        foreach (var job in package.Jobs)
        {
            if (string.IsNullOrWhiteSpace(job.PortableJobId))
                return new(null, "Every packaged job requires a portable job id.");
            if (job.Definition is null)
                return new(null, $"Packaged job '{job.PortableJobId}' has no definition.");
            if (string.IsNullOrWhiteSpace(job.Definition.Name))
                return new(null, $"Packaged job '{job.PortableJobId}' requires a name.");
            if (!IsValidTaskName(job.Definition.TaskName))
                return new(null, $"Packaged job '{job.PortableJobId}' has an invalid task name.");
            if (job.Definition.PromptTemplate is null || job.Definition.PromptTemplate.Length > MaxPromptChars)
                return new(null, $"Packaged job '{job.PortableJobId}' has an invalid prompt.");
            if (job.Definition.Cadence is null ||
                job.Definition.Cadence.Days is null ||
                job.Definition.Skills is null)
            {
                return new(null, $"Packaged job '{job.PortableJobId}' has incomplete cadence or skill metadata.");
            }
            if (job.Definition.WorkingDirectoryBindingId is not null &&
                !bindingIds.Contains(job.Definition.WorkingDirectoryBindingId))
            {
                return new(null, $"Packaged job '{job.PortableJobId}' references an unknown working-directory binding.");
            }
            var unknownBinding = RequiredBindingIds(job.Definition)
                .FirstOrDefault(bindingId => !bindingIds.Contains(bindingId));
            if (unknownBinding is not null)
                return new(null, $"Packaged job '{job.PortableJobId}' references unknown binding '{unknownBinding}'.");
            var unknownDependency = job.Definition.Skills
                .FirstOrDefault(skill => !dependencyIds.Contains(skill.DependencyId));
            if (unknownDependency is not null)
                return new(null, $"Packaged job '{job.PortableJobId}' references an unknown skill dependency.");

            var actualFingerprint = FingerprintDefinition(job.Definition);
            if (!string.Equals(actualFingerprint, job.DefinitionFingerprint, StringComparison.OrdinalIgnoreCase))
                return new(null, $"Packaged job '{job.PortableJobId}' definition fingerprint does not match its content.");
        }

        return new(package, null);
    }

    private static NormalizedValues NormalizeBindingValues(
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

    private static NormalizedJobOptions NormalizeJobOptions(
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
                option => string.Equals(option.PortableJobId, job.PortableJobId, StringComparison.Ordinal))
                ?? new ScheduledJobPackageJobOptions(job.PortableJobId, null, false, false);
        }

        return new(values, null);
    }

    private ScheduledJobPackageDependency BuildSkillDependency(
        ScheduledJobSkill skill,
        string? workingDirectoryBindingId,
        ISet<string> usedDependencyIds,
        IReadOnlyDictionary<string, PluginSkillLocation> installedSkills,
        ICollection<string> warnings)
    {
        var idBase = $"skill-{Slug(skill.Skill)}";
        var id = idBase;
        var suffix = 2;
        while (!usedDependencyIds.Add(id))
            id = $"{idBase}-{suffix++}";
        if (skill.Resolution == SkillResolution.Plugin)
        {
            var location = SkillNameCandidates(skill.Skill)
                .Select(candidate => installedSkills.TryGetValue(candidate, out var found) ? found : null)
                .FirstOrDefault(found => found is not null);
            if (location is null)
                warnings.Add($"Plugin source for skill '{skill.Skill}' could not be identified.");

            return new ScheduledJobPackageDependency(
                id,
                ScheduledJobPackageDependencyKind.PluginSkill,
                skill.Skill,
                true,
                location?.PluginName,
                location?.Marketplace,
                location?.Version,
                null,
                null,
                "Install a plugin that provides this skill before enabling the imported job.");
        }

        if (skill.Resolution == SkillResolution.RepoLocal)
        {
            var normalized = SkillNameCandidates(skill.Skill).Last();
            return new ScheduledJobPackageDependency(
                id,
                ScheduledJobPackageDependencyKind.RepoLocalSkill,
                skill.Skill,
                true,
                null,
                null,
                null,
                workingDirectoryBindingId,
                $".github/skills/{normalized}/SKILL.md",
                "Map the working repository and confirm that it contains this skill.");
        }

        warnings.Add($"Skill '{skill.Skill}' has unknown resolution.");
        return new ScheduledJobPackageDependency(
            id,
            ScheduledJobPackageDependencyKind.Configuration,
            skill.Skill,
            true,
            null,
            null,
            null,
            workingDirectoryBindingId,
            null,
            "Skill resolution is unknown and must be verified manually.");
    }

    private string GetOrAddPathBinding(
        string path,
        string description,
        ScheduledJobPackageProfile profile,
        ICollection<ScheduledJobPackageBinding> bindings,
        IDictionary<string, string> pathBindings)
    {
        if (pathBindings.TryGetValue(path, out var existing))
            return existing;

        var baseName = fileSystem.Path.GetFileName(path.TrimEnd('\\', '/'));
        if (string.IsNullOrWhiteSpace(baseName))
            baseName = "path";
        var idBase = Slug(baseName);
        var id = idBase;
        var suffix = 2;
        var used = bindings.Select(binding => binding.Id).ToHashSet(StringComparer.Ordinal);
        while (used.Contains(id))
            id = $"{idBase}-{suffix++}";

        var kind = fileSystem.Directory.Exists(path)
            ? ScheduledJobPackageBindingKind.Directory
            : fileSystem.File.Exists(path)
                ? ScheduledJobPackageBindingKind.File
                : ScheduledJobPackageBindingKind.Path;
        bindings.Add(new ScheduledJobPackageBinding(
            id,
            kind,
            description,
            true,
            profile == ScheduledJobPackageProfile.Transfer ? path : null,
            null,
            null));
        pathBindings[path] = id;
        return id;
    }

    private string? TokenizeText(
        string? text,
        string description,
        string? workingDirectory,
        string? workingDirectoryBindingId,
        ScheduledJobPackageProfile profile,
        ICollection<ScheduledJobPackageBinding> bindings,
        IDictionary<string, string> pathBindings)
    {
        if (text is null)
            return null;

        var result = text;
        if (workingDirectory is not null && workingDirectoryBindingId is not null)
        {
            result = ReplacePathWithToken(
                result,
                workingDirectory,
                BindingToken(workingDirectoryBindingId));
        }

        foreach (var path in FindAbsolutePaths(result).OrderByDescending(path => path.Length))
        {
            var bindingId = GetOrAddPathBinding(
                path,
                description,
                profile,
                bindings,
                pathBindings);
            result = ReplacePathWithToken(result, path, BindingToken(bindingId));
        }

        return result;
    }

    private string? ValidateBinding(ScheduledJobPackageBinding binding, string? value)
    {
        if (value is null)
            return binding.Required ? "A destination value is required." : null;

        return binding.Kind switch
        {
            ScheduledJobPackageBindingKind.Directory or ScheduledJobPackageBindingKind.Repository
                when !fileSystem.Directory.Exists(value) => "The destination directory does not exist.",
            ScheduledJobPackageBindingKind.File
                when !fileSystem.File.Exists(value) => "The destination file does not exist.",
            ScheduledJobPackageBindingKind.Path
                when !fileSystem.Directory.Exists(value) && !fileSystem.File.Exists(value) =>
                    "The destination path does not exist.",
            _ => null,
        };
    }

    private bool BindingExists(ScheduledJobPackageBindingKind kind, string value) =>
        kind switch
        {
            ScheduledJobPackageBindingKind.Directory or ScheduledJobPackageBindingKind.Repository =>
                fileSystem.Directory.Exists(value),
            ScheduledJobPackageBindingKind.File => fileSystem.File.Exists(value),
            ScheduledJobPackageBindingKind.Path =>
                fileSystem.Directory.Exists(value) || fileSystem.File.Exists(value),
            _ => !string.IsNullOrWhiteSpace(value),
        };

    private IReadOnlyDictionary<string, PluginSkillLocation> DiscoverInstalledPluginSkills()
    {
        var result = new Dictionary<string, PluginSkillLocation>(StringComparer.OrdinalIgnoreCase);
        if (!fileSystem.Directory.Exists(options.InstalledPluginsPath))
            return result;

        string[] skillFiles;
        try
        {
            skillFiles = fileSystem.Directory.GetFiles(
                options.InstalledPluginsPath,
                "SKILL.md",
                SearchOption.AllDirectories);
        }
        catch (UnauthorizedAccessException)
        {
            return result;
        }
        catch (IOException)
        {
            return result;
        }

        foreach (var file in skillFiles)
        {
            var skillDirectory = fileSystem.Path.GetDirectoryName(file);
            if (skillDirectory is null)
                continue;
            var skillName = fileSystem.Path.GetFileName(skillDirectory);
            var relative = fileSystem.Path.GetRelativePath(options.InstalledPluginsPath, file);
            var parts = relative.Split(
                [fileSystem.Path.DirectorySeparatorChar, fileSystem.Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 4)
                continue;

            result.TryAdd(
                skillName,
                new PluginSkillLocation(
                    parts[1],
                    parts[0],
                    ReadPluginVersion(fileSystem.Path.Combine(
                        options.InstalledPluginsPath,
                        parts[0],
                        parts[1]))));
        }

        return result;
    }

    private string? ReadPluginVersion(string pluginRoot)
    {
        var marketplacePath = fileSystem.Path.Combine(pluginRoot, ".claude-plugin", "marketplace.json");
        if (!fileSystem.File.Exists(marketplacePath))
            return null;

        try
        {
            using var document = JsonDocument.Parse(fileSystem.File.ReadAllText(marketplacePath));
            return document.RootElement.TryGetProperty("metadata", out var metadata) &&
                   metadata.TryGetProperty("version", out var version)
                ? version.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static IReadOnlyList<string> RequiredBindingIds(ScheduledJobPortableDefinition definition)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        if (definition.WorkingDirectoryBindingId is not null)
            result.Add(definition.WorkingDirectoryBindingId);
        AddBindingTokens(definition.Description, result);
        AddBindingTokens(definition.PromptTemplate, result);
        AddBindingTokens(definition.AllowFlags, result);
        AddBindingTokens(definition.CopilotArgs, result);
        return result.ToArray();
    }

    private static void AddBindingTokens(
        string? value,
        ISet<string> result)
    {
        if (value is null)
            return;
        foreach (Match match in BindingTokenRegex().Matches(value))
            result.Add(match.Groups["id"].Value);
    }

    private static IReadOnlyList<string> FindAbsolutePaths(string text)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in QuotedAbsolutePathRegex().Matches(text))
            result.Add(match.Groups["path"].Value.TrimEnd('.', ':'));
        foreach (Match match in UnquotedAbsolutePathRegex().Matches(text))
            result.Add(match.Groups["path"].Value.TrimEnd('.', ':'));
        return result.Where(path => !path.Contains(BindingTokenPrefix, StringComparison.Ordinal)).ToArray();
    }

    private static IReadOnlyList<string> SkillNameCandidates(string skill)
    {
        var separator = skill.LastIndexOf(':');
        return separator >= 0 && separator < skill.Length - 1
            ? [skill, skill[(separator + 1)..]]
            : [skill];
    }

    private static IEnumerable<string> RepoLocalSkillPaths(string cwd, string skill) =>
    [
        Path.Combine(cwd, ".github", "skills", skill, "SKILL.md"),
        Path.Combine(cwd, ".claude", "skills", skill, "SKILL.md"),
        Path.Combine(cwd, "skills", skill, "SKILL.md"),
    ];

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

    private static string FingerprintDefinition(ScheduledJobPortableDefinition definition) =>
        Sha256(JsonSerializer.Serialize(
            definition,
            ScheduledJobPackageJsonContext.Default.ScheduledJobPortableDefinition));

    private static string PreviewFingerprint(
        string packageFingerprint,
        string destinationTimeZone,
        IReadOnlyList<ScheduledJobPackageBindingPreview> bindings,
        IReadOnlyList<ScheduledJobPackageJobPreview> jobs,
        IReadOnlyDictionary<string, ScheduledJobPackageJobOptions> options)
    {
        var builder = new StringBuilder(packageFingerprint)
            .Append('|')
            .Append(destinationTimeZone);
        foreach (var binding in bindings.OrderBy(binding => binding.Id, StringComparer.Ordinal))
        {
            builder.Append('|')
                .Append(binding.Id)
                .Append('=')
                .Append(binding.ResolvedValue)
                .Append(':')
                .Append(binding.Error);
        }

        foreach (var job in jobs.OrderBy(job => job.PortableJobId, StringComparer.Ordinal))
        {
            var jobOptions = options[job.PortableJobId];
            builder.Append('|')
                .Append(job.PortableJobId)
                .Append(':')
                .Append(job.TargetTaskName)
                .Append(':')
                .Append(job.Status)
                .Append(':')
                .Append(jobOptions.AllowDuplicate);
            builder.Append(':').Append(jobOptions.Skip);
            foreach (var finding in job.Findings.OrderBy(finding => finding.Code, StringComparer.Ordinal))
                builder.Append(':').Append(finding.Code).Append('=').Append(finding.Message);
        }

        return Sha256(builder.ToString());
    }

    private static string StablePackageId(
        IReadOnlyList<string> ids,
        ScheduledJobPackageProfile profile)
    {
        var source = $"{profile}:{string.Join("|", ids.OrderBy(id => id, StringComparer.Ordinal))}";
        return $"narnia-{Sha256(source)[..24].ToLowerInvariant()}";
    }

    private static string CurrentVersion() =>
        typeof(ScheduledJobPackageService).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion
        ?? typeof(ScheduledJobPackageService).Assembly.GetName().Version?.ToString()
        ?? "unknown";

    private static string BindingToken(string id) =>
        $"{BindingTokenPrefix}{id}{BindingTokenSuffix}";

    private static string? RenderText(
        string? value,
        IReadOnlyDictionary<string, string> bindings)
    {
        if (value is null)
            return null;

        var result = value;
        foreach (var binding in bindings)
            result = result.Replace(BindingToken(binding.Key), binding.Value, StringComparison.Ordinal);
        return result;
    }

    private static string ReplacePathWithToken(
        string text,
        string path,
        string token)
    {
        var normalizedPath = path.Length > 3
            ? path.TrimEnd('\\', '/')
            : path;
        var hasTrailingSeparator = normalizedPath.EndsWith('\\') || normalizedPath.EndsWith('/');
        var boundary = hasTrailingSeparator
            ? ""
            : """(?=$|[\\/\s"',.;:)\]}])""";
        var pattern = $"""(?<![A-Za-z0-9_]){Regex.Escape(normalizedPath)}{boundary}""";
        return Regex.Replace(
            text,
            pattern,
            _ => token,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string Slug(string value)
    {
        var builder = new StringBuilder(value.Length);
        var previousHyphen = false;
        foreach (var character in value.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
                previousHyphen = false;
            }
            else if (!previousHyphen)
            {
                builder.Append('-');
                previousHyphen = true;
            }
        }

        var result = builder.ToString().Trim('-');
        return result.Length == 0 ? "value" : result;
    }

    private static bool IsValidTaskName(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= 240 &&
        !value.Any(character =>
            character is '\\' or '/' ||
            char.IsControl(character));

    private static bool IsPathCoveredByBinding(
        string path,
        string bindingValue)
    {
        var normalizedBinding = bindingValue.Length > 3
            ? bindingValue.TrimEnd('\\', '/')
            : bindingValue;
        if (!path.StartsWith(normalizedBinding, StringComparison.OrdinalIgnoreCase))
            return false;
        if (path.Length == normalizedBinding.Length)
            return true;
        if (normalizedBinding.EndsWith('\\') || normalizedBinding.EndsWith('/'))
            return true;

        return path[normalizedBinding.Length] is '\\' or '/';
    }

    private static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static ScheduledJobPackageFinding Error(
        string code,
        string message,
        string? bindingId = null) =>
        new(code, ScheduledJobPackageFindingSeverity.Error, message, bindingId);

    private static ScheduledJobPackageFinding Warning(
        string code,
        string message,
        string? bindingId = null) =>
        new(code, ScheduledJobPackageFindingSeverity.Warning, message, bindingId);

    private static ScheduledJobPackageExportResult ExportFailure(string error) =>
        new(false, error, null, null, []);

    private static ScheduledJobPackagePreviewResult PreviewFailure(string error) =>
        new(false, error, null, null, null, [], []);

    private static ScheduledJobPackageImportResult ImportFailure(string error) =>
        new(false, error, [], null);

    private sealed record PackageSourceJob(
        ScheduledJobDefinition Definition,
        string? SourceJobId,
        string? SourceTaskName,
        bool? SourceEnabled,
        string PortableJobId);

    private sealed record PluginSkillLocation(
        string PluginName,
        string Marketplace,
        string? Version);

    private sealed record ParsedPackage(
        ScheduledJobPackage? Package,
        string? Error);

    private sealed record NormalizedValues(
        IReadOnlyDictionary<string, string>? Values,
        string? Error);

    private sealed record NormalizedJobOptions(
        IReadOnlyDictionary<string, ScheduledJobPackageJobOptions>? Values,
        string? Error);

    private sealed record RenderDefinitionResult(
        ScheduledJobDefinition? Definition,
        string? Error);

    [GeneratedRegex("""(?<quote>["'])(?<path>(?:[A-Za-z]:\\|\\\\)[^"'\r\n]+)\k<quote>""")]
    private static partial Regex QuotedAbsolutePathRegex();

    [GeneratedRegex("""(?<![A-Za-z0-9_])(?<path>(?:[A-Za-z]:\\|\\\\)[^"'\s\r\n,;)\]}]+)""")]
    private static partial Regex UnquotedAbsolutePathRegex();

    [GeneratedRegex(@"\{\{narnia:(?<id>[a-z0-9-]+)\}\}")]
    private static partial Regex BindingTokenRegex();

    [GeneratedRegex("^[a-z0-9](?:[a-z0-9-]{0,78}[a-z0-9])?$")]
    private static partial Regex BindingIdRegex();

    [GeneratedRegex(
        """(?i)(?:api[_-]?key|token|password|secret)\s*[:=]\s*["']?[A-Za-z0-9/+_.-]{8,}""")]
    private static partial Regex SecretLikeValueRegex();
}
