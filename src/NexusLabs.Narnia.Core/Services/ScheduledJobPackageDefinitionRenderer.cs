using System.IO.Abstractions;
using NexusLabs.Narnia.Core.Models;

namespace NexusLabs.Narnia.Core.Services;

internal sealed class ScheduledJobPackageDefinitionRenderer(IFileSystem fileSystem)
{
    public IReadOnlyList<ScheduledJobPackageBindingPreview> ResolveBindings(
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

    public static ScheduledJobPackageRenderResult RenderDefinition(
        ScheduledJobPortableDefinition portable,
        string targetTaskName,
        IReadOnlyDictionary<string, string> bindings)
    {
        var prompt = portable.PromptTemplate;
        foreach (var binding in bindings)
        {
            prompt = prompt.Replace(
                ScheduledJobPackageText.BindingToken(binding.Key),
                binding.Value,
                StringComparison.Ordinal);
        }

        var description = ScheduledJobPackageText.RenderText(portable.Description, bindings);
        var allowFlags = ScheduledJobPackageText.RenderText(portable.AllowFlags, bindings);
        var copilotArgs = ScheduledJobPackageText.RenderText(portable.CopilotArgs, bindings);
        if (new[] { prompt, description, allowFlags, copilotArgs }
            .Any(value => value?.Contains("{{narnia:", StringComparison.Ordinal) == true))
        {
            return new(null, "The rendered definition still contains unresolved Narnia binding tokens.");
        }

        string? cwd = null;
        if (portable.WorkingDirectoryBindingId is not null &&
            !bindings.TryGetValue(portable.WorkingDirectoryBindingId, out cwd))
        {
            return new(
                null,
                $"Working-directory binding '{portable.WorkingDirectoryBindingId}' is unresolved.");
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

        var definition = new ScheduledJobDefinition(
            portable.Name,
            description,
            cwd,
            prompt,
            allowFlags,
            copilotArgs,
            targetTaskName,
            new ScheduleCadence(
                portable.Cadence.Kind,
                time,
                days,
                portable.Cadence.DayOfMonth),
            portable.Skills
                .OrderBy(skill => skill.Order)
                .Select((skill, order) => new ScheduledJobSkill(
                    skill.Skill,
                    skill.Resolution,
                    order))
                .ToArray());
        return new(definition, null);
    }

    public static void InspectRenderedDefinition(
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
        if (ScheduledJobPackageText.ContainsCredentialLikeLiteral(allDefinitionText))
        {
            findings.Add(new ScheduledJobPackageFinding(
                "possible-secret",
                ScheduledJobPackageFindingSeverity.Warning,
                "The rendered definition contains text that resembles an embedded credential. Review it before sharing or importing.",
                null));
        }

        var boundPaths = resolvedBindingValues.ToArray();
        var unboundPaths = ScheduledJobPackageText.FindAbsolutePaths(allDefinitionText)
            .Where(path => !boundPaths.Any(bound =>
                ScheduledJobPackageText.IsPathCoveredByBinding(path, bound)))
            .ToArray();
        if (unboundPaths.Length > 0)
        {
            findings.Add(new ScheduledJobPackageFinding(
                "unbound-absolute-path",
                ScheduledJobPackageFindingSeverity.Warning,
                "The rendered prompt still contains an absolute path. Confirm that it is valid on this machine.",
                null));
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

    private string? ValidateBinding(
        ScheduledJobPackageBinding binding,
        string? value)
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

    private bool BindingExists(
        ScheduledJobPackageBindingKind kind,
        string value) =>
        kind switch
        {
            ScheduledJobPackageBindingKind.Directory or ScheduledJobPackageBindingKind.Repository =>
                fileSystem.Directory.Exists(value),
            ScheduledJobPackageBindingKind.File => fileSystem.File.Exists(value),
            ScheduledJobPackageBindingKind.Path =>
                fileSystem.Directory.Exists(value) || fileSystem.File.Exists(value),
            _ => !string.IsNullOrWhiteSpace(value),
        };
}

internal sealed record ScheduledJobPackageRenderResult(
    ScheduledJobDefinition? Definition,
    string? Error);
