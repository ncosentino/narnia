using NexusLabs.Narnia.Core.Models;

namespace NexusLabs.Narnia.Core.Services;

/// <summary>
/// Reads and repairs Copilot's per-workspace sidebar tab lists.
/// </summary>
/// <remarks>
/// Copilot restores these tabs when a workspace is reopened and rewrites the list from its own
/// in-memory state when it shuts down. A repair therefore only survives once no Copilot runtime
/// still owns a tab in the workspace.
/// </remarks>
public interface ICopilotSidebarTabsService
{
    /// <summary>Lists every workspace that has a persisted sidebar tab list.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Workspaces ordered by descending tab count, then by working directory.</returns>
    ValueTask<IReadOnlyList<CopilotSidebarWorkspace>> ListAsync(CancellationToken ct);

    /// <summary>Gets the sidebar tab list for one working directory.</summary>
    /// <param name="cwd">Working directory exactly as Copilot recorded it.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The workspace, or <see langword="null"/> when it has no persisted tab list.</returns>
    ValueTask<CopilotSidebarWorkspace?> GetAsync(string cwd, CancellationToken ct);

    /// <summary>Removes specific sessions from a workspace's sidebar tab list.</summary>
    /// <param name="cwd">Working directory exactly as Copilot recorded it.</param>
    /// <param name="sessionIds">Sessions to drop from the list.</param>
    /// <param name="force">Applies the repair even when a Copilot runtime would overwrite it.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The repair outcome.</returns>
    ValueTask<CopilotSidebarRepairResult> RemoveTabsAsync(
        string cwd,
        IReadOnlyCollection<string> sessionIds,
        bool force,
        CancellationToken ct);

    /// <summary>Clears a workspace's entire sidebar tab list.</summary>
    /// <param name="cwd">Working directory exactly as Copilot recorded it.</param>
    /// <param name="force">Applies the repair even when a Copilot runtime would overwrite it.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The repair outcome.</returns>
    ValueTask<CopilotSidebarRepairResult> ResetAsync(
        string cwd,
        bool force,
        CancellationToken ct);
}
