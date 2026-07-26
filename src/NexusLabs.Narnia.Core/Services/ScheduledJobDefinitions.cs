using NexusLabs.Narnia.Core.Models;

namespace NexusLabs.Narnia.Core.Services;

/// <summary>Result of normalizing a scheduled-job boundary or persisted model.</summary>
/// <param name="Definition">Normalized definition on success.</param>
/// <param name="Error">Validation failure.</param>
public sealed record ScheduledJobDefinitionResult(
    ScheduledJobDefinition? Definition,
    string? Error);

/// <summary>Shared normalization and mapping helpers for scheduled-job definitions.</summary>
public static class ScheduledJobDefinitions
{
    /// <summary>Validates and normalizes a create/update input.</summary>
    public static ScheduledJobDefinitionResult FromInput(ScheduledJobInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Name))
            return new(null, "A job name is required.");
        if (string.IsNullOrWhiteSpace(input.Prompt))
            return new(null, "A prompt is required (it is what Copilot runs).");

        var cadence = ParseCadence(input.CadenceKind, input.Time, input.Days, input.DayOfMonth);
        var taskName = string.IsNullOrWhiteSpace(input.TaskName) ? input.Name.Trim() : input.TaskName.Trim();
        var skills = NormalizeSkills(input.Skills);
        return new(
            new ScheduledJobDefinition(
                input.Name.Trim(),
                NullIfWhiteSpace(input.Description),
                NullIfWhiteSpace(input.Cwd),
                input.Prompt,
                NullIfWhiteSpace(input.AllowFlags),
                NullIfWhiteSpace(input.CopilotArgs),
                taskName,
                cadence,
                skills),
            null);
    }

    /// <summary>Converts a persisted scheduled job into its portable behavioral definition.</summary>
    public static ScheduledJobDefinitionResult FromJob(ScheduledJob job)
    {
        if (string.IsNullOrWhiteSpace(job.Prompt))
            return new(null, $"Scheduled job '{job.Name}' has no stored prompt and cannot be exported.");

        var days = job.CadenceDays?
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        int? dayOfMonth = null;
        if (string.Equals(job.CadenceKind, "Monthly", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(job.CadenceDays, out var parsedDay))
        {
            dayOfMonth = parsedDay;
            days = null;
        }

        return new(
            new ScheduledJobDefinition(
                job.Name,
                job.Description,
                job.Cwd,
                job.Prompt,
                job.AllowFlags,
                job.CopilotArgs,
                job.TaskName,
                ParseCadence(job.CadenceKind, job.CadenceTime, days, dayOfMonth),
                job.Skills),
            null);
    }

    /// <summary>Converts a normalized definition into the existing create/update boundary shape.</summary>
    public static ScheduledJobInput ToInput(ScheduledJobDefinition definition) =>
        new(
            Name: definition.Name,
            Description: definition.Description,
            Cwd: definition.WorkingDirectory,
            Prompt: definition.Prompt,
            AllowFlags: definition.AllowFlags,
            CopilotArgs: definition.CopilotArgs,
            TaskName: definition.TaskName,
            CadenceKind: definition.Cadence.Kind.ToString(),
            Time: definition.Cadence.TimeOfDay.ToString("HH\\:mm"),
            Days: definition.Cadence.DaysOfWeek.Select(day => day.ToString()).ToArray(),
            DayOfMonth: definition.Cadence.DayOfMonth,
            Skills: definition.Skills
                .OrderBy(skill => skill.Order)
                .Select(skill => new ScheduledJobSkillInput(skill.Skill, skill.Resolution.ToString()))
                .ToArray());

    private static ScheduleCadence ParseCadence(
        string? cadenceKind,
        string? time,
        IReadOnlyList<string>? days,
        int? dayOfMonth)
    {
        var parsedTime = TimeOnly.TryParse(time, out var value) ? value : new TimeOnly(5, 0);
        var kind = cadenceKind?.ToLowerInvariant() switch
        {
            "weekly" => ScheduleCadenceKind.Weekly,
            "monthly" => ScheduleCadenceKind.Monthly,
            _ => ScheduleCadenceKind.Daily,
        };
        var parsedDays = (days ?? [])
            .Select(day => Enum.TryParse<DayOfWeek>(day, ignoreCase: true, out var parsed)
                ? parsed
                : (DayOfWeek?)null)
            .Where(day => day is not null)
            .Select(day => day!.Value)
            .Distinct()
            .ToArray();
        var parsedDayOfMonth = dayOfMonth is >= 1 and <= 31 ? dayOfMonth.Value : 1;
        return new ScheduleCadence(kind, parsedTime, parsedDays, parsedDayOfMonth);
    }

    private static IReadOnlyList<ScheduledJobSkill> NormalizeSkills(
        IReadOnlyList<ScheduledJobSkillInput>? inputs)
    {
        var result = new List<ScheduledJobSkill>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var input in inputs ?? [])
        {
            var skillName = input.Skill.Trim();
            if (skillName.Length == 0 || !seen.Add(skillName))
                continue;

            var resolution = Enum.TryParse<SkillResolution>(
                input.Resolution,
                ignoreCase: true,
                out var parsed)
                ? parsed
                : SkillResolution.Unknown;
            result.Add(new ScheduledJobSkill(skillName, resolution, result.Count));
        }

        return result;
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
