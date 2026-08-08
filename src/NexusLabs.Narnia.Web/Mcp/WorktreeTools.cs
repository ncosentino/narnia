using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using NexusLabs.Narnia.Core.Models;
using NexusLabs.Narnia.Core.Services;

namespace NexusLabs.Narnia.Web.Mcp;

[McpServerToolType]
internal sealed class WorktreeTools(ISessionWorktreeAdvisor worktreeAdvisor)
{
    [McpServerTool(Name = "get_session_worktrees")]
    [Description("Lists the Git worktrees a session could launch into and reports where its Narnia branch override disagrees with real Git state. Read-only: no branch is ever checked out. Use this to find sessions that look separated by their branch label but actually share one working tree.")]
    public async Task<string> GetSessionWorktreesAsync(
        [Description("Copilot session ID (GUID).")] string sessionId,
        CancellationToken cancellationToken)
    {
        var advice = await worktreeAdvisor.AdviseAsync(sessionId, cancellationToken);
        return JsonSerializer.Serialize(advice, McpJsonContext.Default.SessionWorktreeAdvice);
    }
}
