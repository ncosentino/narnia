namespace NexusLabs.Narnia.Core.Models;

/// <summary>
/// The portable behavioral definition of a scheduled Copilot job. Machine-owned values such as the
/// Narnia job id, generated wrapper paths, log directory, and OS task identity are deliberately
/// excluded so the same definition can be materialized safely on another computer.
/// </summary>
/// <param name="Name">User-facing display name.</param>
/// <param name="Description">What the job does.</param>
/// <param name="WorkingDirectory">Directory Copilot runs from, or <c>null</c> when none is required.</param>
/// <param name="Prompt">The complete prompt passed to <c>copilot -p</c>.</param>
/// <param name="AllowFlags">Copilot allow flags.</param>
/// <param name="CopilotArgs">Additional Copilot arguments.</param>
/// <param name="TaskName">Preferred destination Task Scheduler name.</param>
/// <param name="Cadence">Normalized firing cadence.</param>
/// <param name="Skills">Ordered skills referenced by the prompt.</param>
public sealed record ScheduledJobDefinition(
    string Name,
    string? Description,
    string? WorkingDirectory,
    string Prompt,
    string? AllowFlags,
    string? CopilotArgs,
    string TaskName,
    ScheduleCadence Cadence,
    IReadOnlyList<ScheduledJobSkill> Skills);
