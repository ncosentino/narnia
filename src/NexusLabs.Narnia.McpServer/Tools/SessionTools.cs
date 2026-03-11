using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using ModelContextProtocol.Server;
using NexusLabs.Narnia.Core.Configuration;
using NexusLabs.Narnia.Core.Repositories;
using NexusLabs.Narnia.McpServer.Serialization;

namespace NexusLabs.Narnia.McpServer.Tools;

[McpServerToolType]
internal sealed class SessionTools
{
    private readonly ISessionRepository _repository;
    private readonly ISessionSearch _search;
    private readonly IWorkspaceReader _workspaceReader;
    private readonly NarniaOptions _options;

    public SessionTools(ISessionRepository repository, ISessionSearch search, IWorkspaceReader workspaceReader, NarniaOptions options)
    {
        _repository = repository;
        _search = search;
        _workspaceReader = workspaceReader;
        _options = options;
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
    [Description("Full-text search across all session content including summaries, conversation turns, and checkpoints. Use FTS5 syntax, e.g. 'dependency injection' or 'auth*'.")]
    public async Task<string> SearchSessionsAsync(
        [Description("Search query. Supports FTS5 syntax e.g. 'dependency injection' or 'auth*'.")] string query,
        [Description("Maximum number of results to return. Default 10.")] int limit = 10,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var results = await _search.SearchAsync(query, limit, cancellationToken);
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
    [Description("List all sessions for a specific git repository.")]
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
    [Description("List all sessions that were started in a specific working directory.")]
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
    [Description("Get workspace metadata for a session: the git root directory and a list of session artifact files (e.g. plan.md, context files) stored in the session's files/ directory.")]
    public Task<string> GetSessionWorkspaceAsync(
        [Description("The session GUID.")] string sessionId,
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        var info = _workspaceReader.ReadWorkspace(sessionId);
        return Task.FromResult(JsonSerializer.Serialize(info, McpJsonContext.Default.WorkspaceInfo));
    }

    [McpServerTool(Name = "open_narnia_ui")]
    [Description("Ensures the Narnia web UI is running and opens it in the default browser. Starts the web server automatically if it is not already running.")]
    public async Task<string> OpenNarniaUiAsync(CancellationToken cancellationToken = default)
    {
        var url = _options.WebUiUrl;
        bool alreadyRunning = await IsWebUiRunningAsync(url, cancellationToken);

        if (!alreadyRunning)
        {
            var startResult = TryStartWebServer();
            if (startResult is not null)
                return startResult;

            bool started = await WaitForWebUiAsync(url, timeoutSeconds: 20, cancellationToken);
            if (!started)
                return $"Started the web server but it did not become reachable at {url} within 20 seconds. Try opening {url} manually.";
        }

        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

        return alreadyRunning
            ? $"Narnia web UI was already running. Opened {url} in your default browser."
            : $"Started the Narnia web UI and opened {url} in your default browser.";
    }

    private static async Task<bool> IsWebUiRunningAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            var response = await client.GetAsync(url, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private string? TryStartWebServer()
    {
        // Try published binary next to the MCP server binary first
        var baseDir = AppContext.BaseDirectory;
        var exeName = OperatingSystem.IsWindows() ? "NexusLabs.Narnia.Web.exe" : "NexusLabs.Narnia.Web";
        var publishedExe = Path.Combine(baseDir, exeName);
        if (File.Exists(publishedExe))
        {
            Process.Start(new ProcessStartInfo(publishedExe) { UseShellExecute = true });
            return null;
        }

        // Resolve project path
        var projectPath = ResolveWebProjectPath();
        if (projectPath is null)
            return "Could not locate the Narnia web project. Set the NARNIA__WebProjectPath environment variable to the path of NexusLabs.Narnia.Web.csproj or its directory.";

        Process.Start(new ProcessStartInfo("dotnet")
        {
            Arguments = $"run --project \"{projectPath}\"",
            UseShellExecute = true
        });
        return null;
    }

    private string? ResolveWebProjectPath()
    {
        if (!string.IsNullOrWhiteSpace(_options.WebProjectPath))
        {
            var p = _options.WebProjectPath;
            if (File.Exists(p) && p.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                return p;
            var candidate = Path.Combine(p, "NexusLabs.Narnia.Web.csproj");
            if (File.Exists(candidate))
                return candidate;
            return null;
        }

        // Walk up from the MCP binary directory looking for the .csproj
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var csproj = Path.Combine(dir.FullName, "src", "NexusLabs.Narnia.Web", "NexusLabs.Narnia.Web.csproj");
            if (File.Exists(csproj))
                return csproj;
            dir = dir.Parent;
        }
        return null;
    }

    private static async Task<bool> WaitForWebUiAsync(string url, int timeoutSeconds, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(1000, cancellationToken);
            if (await IsWebUiRunningAsync(url, cancellationToken))
                return true;
        }
        return false;
    }
}
