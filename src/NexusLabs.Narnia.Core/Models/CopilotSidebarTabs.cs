namespace NexusLabs.Narnia.Core.Models;

/// <summary>
/// One session entry in a workspace's persisted Copilot sidebar tab list, enriched with whatever
/// Narnia knows about that session.
/// </summary>
/// <param name="SessionId">Session identifier exactly as Copilot recorded it.</param>
/// <param name="Position">Zero-based position in the persisted list; Copilot restores in this order.</param>
/// <param name="IsKnown">Whether the session still exists in the Copilot session store.</param>
/// <param name="Title">Session title when known.</param>
/// <param name="Repository">Owning repository when known.</param>
/// <param name="IsLive">Whether a Copilot runtime currently holds this session.</param>
/// <param name="EventStreamBytes">Size of the session's event stream when measurable.</param>
public sealed record CopilotSidebarTab(
    string SessionId,
    int Position,
    bool IsKnown,
    string? Title,
    string? Repository,
    bool IsLive,
    long? EventStreamBytes);

/// <summary>
/// A single workspace's persisted Copilot sidebar tab list. Copilot stores one of these per
/// working directory and replays it as sidebar tabs the next time that folder is opened.
/// </summary>
/// <param name="Cwd">Working directory the tab list belongs to.</param>
/// <param name="FilePath">Full path of the backing state file.</param>
/// <param name="FileName">State file name, which is <c>SHA256(UTF8(cwd))</c> in lowercase hex.</param>
/// <param name="SchemaVersion">Schema version Copilot wrote, when readable.</param>
/// <param name="Tabs">Restored tabs in persisted order.</param>
/// <param name="CwdExists">Whether the working directory still exists on disk.</param>
/// <param name="LastWrittenAt">Last write time of the state file.</param>
/// <param name="ParseError">Populated when the file could not be read or parsed.</param>
public sealed record CopilotSidebarWorkspace(
    string Cwd,
    string FilePath,
    string FileName,
    int? SchemaVersion,
    IReadOnlyList<CopilotSidebarTab> Tabs,
    bool CwdExists,
    DateTimeOffset? LastWrittenAt,
    string? ParseError)
{
    /// <summary>Number of tabs Copilot would restore for this workspace.</summary>
    public int TabCount => Tabs.Count;

    /// <summary>Tabs whose session no longer exists in the session store.</summary>
    public int UnknownTabCount => Tabs.Count(tab => !tab.IsKnown);

    /// <summary>Tabs currently held by a live Copilot runtime.</summary>
    public int LiveTabCount => Tabs.Count(tab => tab.IsLive);

    /// <summary>
    /// Whether repairing this workspace would be undone. Copilot merges its in-memory tab list
    /// back over this file when it shuts down, so a repair only sticks once no runtime owns a tab
    /// in the workspace.
    /// </summary>
    public bool HasLiveRuntime => LiveTabCount > 0;
}

/// <summary>Outcome of a sidebar tab-list repair.</summary>
/// <param name="Cwd">Workspace that was targeted.</param>
/// <param name="Succeeded">Whether the repair was applied.</param>
/// <param name="RemovedSessionIds">Session identifiers removed from the tab list.</param>
/// <param name="RemainingTabCount">Tabs left in the list after the repair.</param>
/// <param name="BackupPath">Path of the backup Narnia wrote before changing anything.</param>
/// <param name="Error">Populated when the repair was refused or failed.</param>
public sealed record CopilotSidebarRepairResult(
    string Cwd,
    bool Succeeded,
    IReadOnlyList<string> RemovedSessionIds,
    int RemainingTabCount,
    string? BackupPath,
    string? Error);
