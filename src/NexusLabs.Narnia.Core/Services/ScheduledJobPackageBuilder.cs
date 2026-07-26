using System.IO.Abstractions;
using NexusLabs.Narnia.Core.Models;

namespace NexusLabs.Narnia.Core.Services;

internal sealed class ScheduledJobPackageBuilder(
    IFileSystem fileSystem,
    ScheduledJobPackageDependencyInspector dependencyInspector)
{
    public ScheduledJobPackageExportResult Build(
        IReadOnlyList<ScheduledJobPackageSourceJob> sourceJobs,
        ScheduledJobPackageProfile profile,
        string packageId,
        IReadOnlyList<ScheduledJobPackageDependency> additionalDependencies)
    {
        var warnings = new List<string>();
        var bindings = new List<ScheduledJobPackageBinding>();
        var dependencies = additionalDependencies.ToList();
        if (dependencies.Any(dependency =>
                string.IsNullOrWhiteSpace(dependency.Id) ||
                !ScheduledJobPackageText.IsValidIdentifier(dependency.Id) ||
                string.IsNullOrWhiteSpace(dependency.Name)) ||
            dependencies.Select(dependency => dependency.Id)
                .Distinct(StringComparer.Ordinal)
                .Count() != dependencies.Count)
        {
            return Failure("Additional dependency ids and names are required and ids must be unique.");
        }

        var usedDependencyIds = dependencies
            .Select(dependency => dependency.Id)
            .ToHashSet(StringComparer.Ordinal);
        var pathBindings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var skillDependencies = new Dictionary<string, ScheduledJobPackageDependency>(StringComparer.Ordinal);
        var installedSkills = dependencyInspector.DiscoverInstalledPluginSkills();
        var packagedJobs = new List<ScheduledJobPackageJob>(sourceJobs.Count);

        foreach (var source in sourceJobs)
        {
            if (string.IsNullOrWhiteSpace(source.Definition.Name))
                return Failure("Every packaged job requires a name.");
            if (string.IsNullOrWhiteSpace(source.Definition.Prompt))
                return Failure($"Scheduled job '{source.Definition.Name}' requires a prompt.");
            if (source.Definition.Prompt.Length > ScheduledJobPackageFormat.MaxPromptChars)
            {
                return Failure(
                    $"Scheduled job '{source.Definition.Name}' exceeds the {ScheduledJobPackageFormat.MaxPromptChars:N0}-character prompt limit.");
            }

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

            if (ScheduledJobPackageText.ContainsCredentialLikeLiteral(string.Join(
                    "\n",
                    source.Definition.Description,
                    source.Definition.Prompt,
                    source.Definition.AllowFlags,
                    source.Definition.CopilotArgs)))
            {
                return Failure(
                    $"Scheduled job '{source.Definition.Name}' contains text that resembles an embedded credential. Move the value into destination-local configuration before packaging the job.");
            }

            var portableSkills = new List<ScheduledJobPackageSkill>();
            foreach (var skill in source.Definition.Skills.OrderBy(skill => skill.Order))
            {
                var dependencyKey = $"{skill.Resolution}:{skill.Skill}";
                if (!skillDependencies.TryGetValue(dependencyKey, out var dependency))
                {
                    dependency = dependencyInspector.BuildSkillDependency(
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
            var definitionFingerprint = ScheduledJobPackageFormat.FingerprintDefinition(portableDefinition);
            var portableJobId = profile == ScheduledJobPackageProfile.Transfer
                ? source.PortableJobId
                : $"job-{ScheduledJobPackageFormat.PackageFingerprint(source.PortableJobId)[..24].ToLowerInvariant()}";
            packagedJobs.Add(new ScheduledJobPackageJob(
                portableJobId,
                definitionFingerprint,
                portableDefinition,
                profile == ScheduledJobPackageProfile.Transfer ? source.SourceJobId : null,
                profile == ScheduledJobPackageProfile.Transfer ? source.SourceTaskName : null,
                profile == ScheduledJobPackageProfile.Transfer ? source.SourceEnabled : null));
        }

        var package = new ScheduledJobPackage(
            ScheduledJobPackageFormat.Format,
            ScheduledJobPackageFormat.SchemaVersion,
            packageId,
            profile,
            DateTimeOffset.UtcNow,
            new ScheduledJobPackageSource(
                ScheduledJobPackageFormat.CurrentVersion(),
                TimeZoneInfo.Local.Id),
            bindings,
            dependencies,
            packagedJobs);
        var json = ScheduledJobPackageFormat.Serialize(package);
        if (json.Length > ScheduledJobPackageFormat.MaxPackageChars)
        {
            return Failure(
                $"The generated package exceeds the {ScheduledJobPackageFormat.MaxPackageChars:N0}-character limit.");
        }

        var validation = ScheduledJobPackageFormat.ParseAndValidate(json);
        if (validation.Error is not null)
            return Failure($"The generated package is invalid: {validation.Error}");

        return new ScheduledJobPackageExportResult(true, null, package, json, warnings);
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
        var idBase = ScheduledJobPackageText.Slug(baseName);
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
            result = ScheduledJobPackageText.ReplacePathWithToken(
                result,
                workingDirectory,
                ScheduledJobPackageText.BindingToken(workingDirectoryBindingId));
        }

        foreach (var path in ScheduledJobPackageText.FindAbsolutePaths(result)
                     .OrderByDescending(path => path.Length))
        {
            var bindingId = GetOrAddPathBinding(
                path,
                description,
                profile,
                bindings,
                pathBindings);
            result = ScheduledJobPackageText.ReplacePathWithToken(
                result,
                path,
                ScheduledJobPackageText.BindingToken(bindingId));
        }

        return result;
    }

    private static ScheduledJobPackageExportResult Failure(string error) =>
        new(false, error, null, null, []);
}

internal sealed record ScheduledJobPackageSourceJob(
    ScheduledJobDefinition Definition,
    string? SourceJobId,
    string? SourceTaskName,
    bool? SourceEnabled,
    string PortableJobId);
