using System.IO.Abstractions;
using System.Text.Json;
using NexusLabs.Narnia.Core.Configuration;
using NexusLabs.Narnia.Core.Models;

namespace NexusLabs.Narnia.Core.Services;

internal sealed class ScheduledJobPackageDependencyInspector(
    NarniaOptions options,
    IFileSystem fileSystem)
{
    public ScheduledJobPackageDependency BuildSkillDependency(
        ScheduledJobSkill skill,
        string? workingDirectoryBindingId,
        ISet<string> usedDependencyIds,
        IReadOnlyDictionary<string, ScheduledJobPluginSkillLocation> installedSkills,
        ICollection<string> warnings)
    {
        var idBase = $"skill-{ScheduledJobPackageText.Slug(skill.Skill)}";
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

    public void InspectSkillDependency(
        ScheduledJobPackageSkill skill,
        string? workingDirectory,
        IReadOnlyDictionary<string, ScheduledJobPluginSkillLocation> installedSkills,
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

    public IReadOnlyDictionary<string, ScheduledJobPluginSkillLocation> DiscoverInstalledPluginSkills()
    {
        var result = new Dictionary<string, ScheduledJobPluginSkillLocation>(StringComparer.OrdinalIgnoreCase);
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
                new ScheduledJobPluginSkillLocation(
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

    private static ScheduledJobPackageFinding Error(
        string code,
        string message) =>
        new(code, ScheduledJobPackageFindingSeverity.Error, message, null);

    private static ScheduledJobPackageFinding Warning(
        string code,
        string message) =>
        new(code, ScheduledJobPackageFindingSeverity.Warning, message, null);
}

internal sealed record ScheduledJobPluginSkillLocation(
    string PluginName,
    string Marketplace,
    string? Version);
