using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using NexusLabs.Narnia.Core.Repositories;

namespace NexusLabs.Narnia.Web.Mcp;

[McpServerToolType]
internal sealed class SessionTools
{
    private readonly ISessionRepository _repository;
    private readonly ISessionSearch _search;
    private readonly IWorkspaceReader _workspaceReader;

    public SessionTools(ISessionRepository repository, ISessionSearch search, IWorkspaceReader workspaceReader)
    {
        _repository = repository;
        _search = search;
        _workspaceReader = workspaceReader;
    }

    [McpServerTool(Name = "list_recent_sessions")]
    [Description("Lists the most recently updated Copilot CLI sessions. Use this to find sessions to resume after a computer restart.")]
    public async Task<string> ListRecentSessionsAsync(
        [Description("Maximum number of sessions to return. Default 20.")] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var sessions = await _repository.ListRecentAsync(limit, ct: cancellationToken);
            return JsonSerializer.Serialize(sessions, McpJsonContext.Default.SessionSummaryArray);
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    [McpServerTool(Name = "search_sessions")]
    [Description("Search visible sessions by Copilot name, Narnia alias, conversation turns, checkpoints, and workspace artifacts. Name and alias matches rank before indexed content. Archived sessions are excluded. This does not filter repository or working-directory metadata; use the exact list tools for those fields.")]
    public async Task<string> SearchSessionsAsync(
        [Description("Session name, Narnia alias, or content query, e.g. 'dependency injection' or the prefix query 'auth*'.")] string query,
        [Description("Maximum number of results to return. Default 10.")] int limit = 10,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var results = await _search.SearchAsync(query, limit, ct: cancellationToken);
            return JsonSerializer.Serialize(results, McpJsonContext.Default.SearchResultArray);
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    [McpServerTool(Name = "get_session_details")]
    [Description("Get full details for a specific session including metadata and statistics.")]
    public async Task<string> GetSessionDetailsAsync(
        [Description("The session GUID, e.g. '0c531e17-1fed-4bb0-a00e-e3e4a08ca6c4'.")] string sessionId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var session = await _repository.GetByIdAsync(sessionId, cancellationToken);
            if (session is null)
                return $"Session '{sessionId}' not found.";
            return JsonSerializer.Serialize(session, McpJsonContext.Default.Session);
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    [McpServerTool(Name = "get_session_checkpoints")]
    [Description("Get all checkpoints for a session. Checkpoints contain structured summaries including overview, history, files changed, and next steps.")]
    public async Task<string> GetSessionCheckpointsAsync(
        [Description("The session GUID.")] string sessionId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var checkpoints = await _repository.GetCheckpointsAsync(sessionId, cancellationToken);
            return JsonSerializer.Serialize(checkpoints, McpJsonContext.Default.CheckpointArray);
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    [McpServerTool(Name = "list_sessions_by_repository")]
    [Description("List visible sessions whose effective remote repository exactly matches an owner/repository value. Narnia repository overrides are applied.")]
    public async Task<string> ListSessionsByRepositoryAsync(
        [Description("Repository in 'owner/repo' format, e.g. 'ncosentino/needlr'.")] string repo,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var sessions = await _repository.ListByRepositoryAsync(repo, ct: cancellationToken);
            return JsonSerializer.Serialize(sessions, McpJsonContext.Default.SessionSummaryArray);
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    [McpServerTool(Name = "list_sessions_by_cwd")]
    [Description("List visible sessions that were started in a specific working directory. Matching follows the operating system's path casing rules and ignores a trailing directory separator.")]
    public async Task<string> ListSessionsByCwdAsync(
        [Description("Working directory path, e.g. 'C:\\dev\\myproject'.")] string cwd,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var sessions = await _repository.ListByCwdAsync(cwd, ct: cancellationToken);
            return JsonSerializer.Serialize(sessions, McpJsonContext.Default.SessionSummaryArray);
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    [McpServerTool(Name = "get_session_turns")]
    [Description("Get conversation turns (messages) for a session. Returns paginated user/assistant message pairs.")]
    public async Task<string> GetSessionTurnsAsync(
        [Description("The session GUID.")] string sessionId,
        [Description("Number of turns to skip for pagination. Default 0.")] int offset = 0,
        [Description("Maximum number of turns to return. Default 10.")] int limit = 10,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var turns = await _repository.GetTurnsAsync(sessionId, offset, limit, cancellationToken);
            return JsonSerializer.Serialize(turns, McpJsonContext.Default.TurnArray);
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    [McpServerTool(Name = "get_session_workspace")]
    [Description("Get read-only Copilot workspace metadata for a session: its Copilot-managed name, whether the user named it, git root, and session artifact files.")]
    public Task<string> GetSessionWorkspaceAsync(
        [Description("The session GUID.")] string sessionId,
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        var info = _workspaceReader.ReadWorkspace(sessionId);
        return Task.FromResult(JsonSerializer.Serialize(info, McpJsonContext.Default.WorkspaceInfo));
    }

}
