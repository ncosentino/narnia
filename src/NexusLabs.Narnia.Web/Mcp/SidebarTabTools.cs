using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using NexusLabs.Narnia.Core.Models;
using NexusLabs.Narnia.Core.Services;

namespace NexusLabs.Narnia.Web.Mcp;

[McpServerToolType]
internal sealed class SidebarTabTools(ICopilotSidebarTabsService sidebarTabs)
{
    [McpServerTool(Name = "list_sidebar_tabs")]
    [Description("Lists Copilot's persisted per-workspace sidebar tab lists. Copilot replays these sessions as sidebar tabs whenever the folder is reopened and renders a preview for each, so an overlong or damaged list is a common cause of broken sidebar rendering that survives /restart.")]
    public async Task<string> ListSidebarTabsAsync(CancellationToken cancellationToken)
    {
        var workspaces = await sidebarTabs.ListAsync(cancellationToken);
        return JsonSerializer.Serialize(
            workspaces.ToArray(),
            McpJsonContext.Default.CopilotSidebarWorkspaceArray);
    }

    [McpServerTool(Name = "repair_sidebar_tabs")]
    [Description("Repairs one workspace's Copilot sidebar tab list, backing up the current list first. Omit sessionIds to clear every tab. No session is deleted. Copilot rewrites this list when it exits, so the repair is refused while a runtime still owns a tab unless force is set.")]
    public async Task<string> RepairSidebarTabsAsync(
        [Description("Working directory exactly as Copilot recorded it, for example C:\\\\dev\\\\nexus-labs\\\\narnia.")] string cwd,
        [Description("Session IDs to remove. Omit or leave empty to clear the entire tab list.")] string[]? sessionIds,
        [Description("Applies the repair even when a live Copilot runtime would overwrite it on exit.")] bool force,
        CancellationToken cancellationToken)
    {
        var result = sessionIds is { Length: > 0 }
            ? await sidebarTabs.RemoveTabsAsync(cwd, sessionIds, force, cancellationToken)
            : await sidebarTabs.ResetAsync(cwd, force, cancellationToken);
        return JsonSerializer.Serialize(
            result,
            McpJsonContext.Default.CopilotSidebarRepairResult);
    }
}
