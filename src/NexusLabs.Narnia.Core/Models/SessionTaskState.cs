namespace NexusLabs.Narnia.Core.Models;

/// <summary>One task recovered from a Copilot session's workspace database.</summary>
/// <param name="Id">Task identifier.</param>
/// <param name="Title">Task title.</param>
/// <param name="Description">Task details.</param>
/// <param name="Status">Recorded task status.</param>
/// <param name="CreatedAt">Recorded creation timestamp, when parseable.</param>
/// <param name="UpdatedAt">Recorded update timestamp, when parseable.</param>
public sealed record SessionTaskItem(
    string Id,
    string Title,
    string? Description,
    string Status,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? UpdatedAt);

/// <summary>One dependency edge recovered from a Copilot session's workspace database.</summary>
/// <param name="TaskId">Dependent task identifier.</param>
/// <param name="DependsOn">Prerequisite task identifier.</param>
public sealed record SessionTaskDependency(string TaskId, string DependsOn);

/// <summary>Read-only task state recovered from a Copilot session workspace.</summary>
/// <param name="Todos">Recovered task rows.</param>
/// <param name="Dependencies">Recovered dependency edges.</param>
/// <param name="Error">Read failure when task state could not be inspected.</param>
public sealed record SessionTaskState(
    IReadOnlyList<SessionTaskItem> Todos,
    IReadOnlyList<SessionTaskDependency> Dependencies,
    string? Error);
