namespace NexusLabs.Narnia.Core.Models;

/// <summary>The lifecycle status of a recorded terminal window.</summary>
public enum TerminalWindowStatus
{
    /// <summary>The window is currently open and being tracked by its terminal process id.</summary>
    Open,

    /// <summary>The window has closed; its composition is retained for recovery.</summary>
    Closed,
}

/// <summary>
/// A terminal window of Copilot tabs recorded for disaster recovery. While open it is
/// tracked by <see cref="TerminalProcessId"/>; once closed it is retained (deduplicated
/// by <see cref="CompositionKey"/>) so the whole window can be reopened later.
/// </summary>
/// <param name="Id">Narnia-assigned stable identifier.</param>
/// <param name="Name">Optional user-supplied name; naming pins the window against retention pruning.</param>
/// <param name="Pinned">When <c>true</c>, the window is never pruned by retention.</param>
/// <param name="Source">Origin of the record (e.g. <c>"live"</c> for the snapshotter).</param>
/// <param name="Status">Whether the window is currently open or closed.</param>
/// <param name="TerminalProcessId">The owning terminal process id while open; <c>null</c> once closed.</param>
/// <param name="CompositionKey">Stable hash of the sorted session-id set, used to dedupe closed records.</param>
/// <param name="OccurrenceCount">How many times this composition has been seen (incremented on dedupe).</param>
/// <param name="FirstSeenAt">When this window was first recorded.</param>
/// <param name="LastSeenAt">When this window was most recently observed.</param>
/// <param name="ClosedAt">When the window closed, or <c>null</c> while open.</param>
/// <param name="Tabs">The Copilot tabs belonging to this window, in tab order.</param>
public sealed record TerminalWindow(
    string Id,
    string? Name,
    bool Pinned,
    string Source,
    TerminalWindowStatus Status,
    int? TerminalProcessId,
    string CompositionKey,
    int OccurrenceCount,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt,
    DateTimeOffset? ClosedAt,
    IReadOnlyList<TerminalWindowTab> Tabs);

/// <summary>
/// A single Copilot session tab belonging to a recorded <see cref="TerminalWindow"/>.
/// </summary>
/// <param name="SessionId">The Copilot session id to resume.</param>
/// <param name="TabOrder">Zero-based position of the tab within its window.</param>
/// <param name="Directory">The captured starting directory, or <c>null</c> when none was recorded.</param>
public sealed record TerminalWindowTab(
    string SessionId,
    int TabOrder,
    string? Directory);
