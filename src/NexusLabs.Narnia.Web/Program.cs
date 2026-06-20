using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Diagnostics;
using System.IO.Abstractions;
using System.Net;
using System.Reflection;
using NexusLabs.Narnia.Core.Configuration;
using NexusLabs.Narnia.Core.Models;
using NexusLabs.Narnia.Core.Repositories;
using NexusLabs.Narnia.Core.Services;
using NexusLabs.Narnia.Web;
using NexusLabs.Narnia.Web.Components;
using NexusLabs.Narnia.Web.Mcp;

var builder = WebApplication.CreateBuilder(args);

var options = new NarniaOptions();
builder.Configuration.GetSection(NarniaOptions.SectionName).Bind(options);

builder.Services.AddSingleton(options);
builder.Services.AddSingleton<IFileSystem, FileSystem>();
builder.Services.AddSingleton<SqliteSessionRepository>();
builder.Services.AddSingleton<ISessionSearch>(sp => sp.GetRequiredService<SqliteSessionRepository>());
builder.Services.AddSingleton<SqliteSessionOverridesRepository>();
builder.Services.AddSingleton<ISessionOverridesRepository>(sp => sp.GetRequiredService<SqliteSessionOverridesRepository>());
builder.Services.AddSingleton<OverridingSessionRepository>();
builder.Services.AddSingleton<ISessionRepository>(sp => sp.GetRequiredService<OverridingSessionRepository>());
builder.Services.AddSingleton<NarniaSettingsDbMigrator>();
builder.Services.AddSingleton<SessionService>();
builder.Services.AddSingleton<IWorkspaceReader, WorkspaceReader>();
builder.Services.AddSingleton<SqliteNarniaSettingsRepository>();
builder.Services.AddSingleton<INarniaSettingsRepository>(sp => sp.GetRequiredService<SqliteNarniaSettingsRepository>());

builder.Services.AddRazorComponents();

builder.Services
    .AddMcpServer()
    .WithHttpTransport(httpOptions => httpOptions.Stateless = true)
    .WithTools<SessionTools>();

var app = builder.Build();

app.Services.GetRequiredService<NarniaSettingsDbMigrator>().MigrateUp();

var serverVersion = Assembly.GetExecutingAssembly()
    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
    ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString();

app.Lifetime.ApplicationStarted.Register(() =>
{
    var addresses = app.Services.GetRequiredService<IServer>()
        .Features.Get<IServerAddressesFeature>()?.Addresses;
    var url = addresses?.FirstOrDefault() ?? string.Empty;
    var port = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Port : 0;
    WebServerRunState.Write(new WebServerRunStateInfo(
        Environment.ProcessId,
        port,
        url,
        serverVersion,
        Environment.ProcessPath,
        DateTimeOffset.UtcNow));
});
app.Lifetime.ApplicationStopping.Register(WebServerRunState.Delete);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error");
    app.UseHsts();
}

app.UseStaticFiles();
app.UseAntiforgery();

// DNS-rebinding defense for the MCP endpoint (MCP spec): the server binds loopback, but a
// malicious web page could still cause a browser to POST here with an attacker Host header.
// Reject /mcp requests whose Host is not a loopback name. Non-browser MCP clients send a
// loopback Host and are unaffected.
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/mcp"))
    {
        var host = context.Request.Host.Host;
        var isLoopback = host is "localhost" or "127.0.0.1" or "::1" or "[::1]";
        if (!isLoopback)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }
    }

    await next(context);
});

app.MapRazorComponents<App>();

app.MapMcp("/mcp").DisableAntiforgery();

app.MapPost("/api/sessions/{id}/overrides", async (
    string id,
    OverrideRequest request,
    ISessionOverridesRepository repo,
    CancellationToken ct) =>
{
    var now = DateTimeOffset.UtcNow;
    var existing = await repo.GetOverrideAsync(id, ct);
    var ov = new SessionOverride(
        id,
        string.IsNullOrWhiteSpace(request.DisplayName) ? null : request.DisplayName.Trim(),
        string.IsNullOrWhiteSpace(request.Repository) ? null : request.Repository.Trim(),
        string.IsNullOrWhiteSpace(request.Branch) ? null : request.Branch.Trim(),
        string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
        existing?.CreatedAt ?? now,
        now)
    {
        IsArchived = existing?.IsArchived ?? false,
        LocalPath = string.IsNullOrWhiteSpace(request.LocalPath) ? null : request.LocalPath.Trim(),
        TerminalTitle = string.IsNullOrWhiteSpace(request.TerminalTitle) ? null : request.TerminalTitle.Trim(),
    };
    await repo.UpsertOverrideAsync(ov, ct);
    return Results.Ok(ov);
});

app.MapDelete("/api/sessions/{id}/overrides", async (
    string id,
    ISessionOverridesRepository repo,
    CancellationToken ct) =>
{
    await repo.DeleteOverrideAsync(id, ct);
    return Results.NoContent();
});

app.MapPost("/api/sessions/{id}/archive", async (
    string id,
    ArchiveRequest request,
    ISessionOverridesRepository repo,
    CancellationToken ct) =>
{
    var now = DateTimeOffset.UtcNow;
    var existing = await repo.GetOverrideAsync(id, ct);
    var ov = new SessionOverride(
        id,
        existing?.DisplayName,
        existing?.Repository,
        existing?.Branch,
        existing?.Notes,
        existing?.CreatedAt ?? now,
        now)
    {
        IsArchived = request.Archived,
        LocalPath = existing?.LocalPath,
        TerminalTitle = existing?.TerminalTitle,
    };
    await repo.UpsertOverrideAsync(ov, ct);
    return Results.Ok();
});

// ── Settings API ────────────────────────────────────────────────────────────
app.MapGet("/api/settings", async (INarniaSettingsRepository repo, CancellationToken ct) =>
    Results.Ok(await repo.GetAllAsync(ct)));

app.MapPost("/api/settings", async (
    SettingRequest request,
    INarniaSettingsRepository repo,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.Key))
        return Results.BadRequest("Key is required");
    await repo.SetAsync(request.Key.Trim(), request.Value?.Trim() ?? "", ct);
    return Results.Ok();
});

app.MapGet("/api/settings/detect-shell", () =>
{
    var path = DetectDefaultShell();
    return path is not null
        ? Results.Ok(new { path })
        : Results.NotFound(new { message = "No shell detected" });
});

// ── Launch API ──────────────────────────────────────────────────────────────
app.MapPost("/api/launch", async (
    LaunchRequest request,
    ISessionRepository sessionRepo,
    ISessionOverridesRepository overridesRepo,
    IWorkspaceReader workspaceReader,
    INarniaSettingsRepository settingsRepo,
    CancellationToken ct) =>
{
    if (!Guid.TryParse(request.SessionId, out _))
        return Results.BadRequest("Invalid session ID format");

    var session = await sessionRepo.GetByIdAsync(request.SessionId, ct);
    if (session is null)
        return Results.NotFound("Session not found");

    var ov = await overridesRepo.GetOverrideAsync(request.SessionId, ct);

    string? directory = null;
    if (request.Target is not "none")
    {
        var workspace = workspaceReader.ReadWorkspace(request.SessionId);

        directory = request.Target switch
        {
            "localPath" => ov?.LocalPath,
            "cwd" => session.Cwd,
            "gitRoot" => workspace?.GitRoot ?? session.GitRoot,
            _ => null,
        };

        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return Results.BadRequest($"Directory not found: {directory ?? "(null)"}");
    }

    var shellPath = await settingsRepo.GetAsync("shell_path", ct) ?? DetectDefaultShell();
    if (string.IsNullOrWhiteSpace(shellPath))
        return Results.BadRequest("No shell configured. Go to Settings to configure one.");

    var title = ov?.TerminalTitle ?? session.Summary ?? $"Narnia: {request.SessionId[..8]}";
    var resumeCmd = $"copilot --resume={request.SessionId}";
    var shellName = Path.GetFileNameWithoutExtension(shellPath).ToLowerInvariant();

    // Prefer Windows Terminal (wt.exe) when available — its --title flag
    // persists even after the child process tries to overwrite it.
    var wtPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Microsoft", "WindowsApps", "wt.exe");
    var useWt = OperatingSystem.IsWindows() && File.Exists(wtPath);

    var psi = new ProcessStartInfo { UseShellExecute = true };

    if (useWt)
    {
        psi.FileName = wtPath;
        var dirArg = directory is not null ? $"--startingDirectory \"{directory}\" " : "";
        // wt new-tab --title keeps the title even when the shell inside tries to change it
        psi.Arguments = $"new-tab --title \"{title.Replace("\"", "\\\"")}\" --suppressApplicationTitle {dirArg}-- \"{shellPath}\" {BuildShellArgs(shellName, resumeCmd)}";
    }
    else
    {
        psi.FileName = shellPath;
        if (directory is not null)
            psi.WorkingDirectory = directory;

        var safeTitle = title.Replace("\"", "\\\"");
        psi.Arguments = shellName switch
        {
            "pwsh" or "powershell" => $"-NoExit -Command \"$host.UI.RawUI.WindowTitle = '{title.Replace("'", "''")}'; {resumeCmd}\"",
            "cmd" => $"/k title {title} & {resumeCmd}",
            _ => $"-c \"printf '\\033]0;{safeTitle}\\007'; {resumeCmd}; exec $SHELL\"",
        };
    }

    try
    {
        Process.Start(psi);
        return Results.Ok(new { launched = true });
    }
    catch (Exception ex)
    {
        return Results.BadRequest($"Failed to launch shell: {ex.Message}");
    }
});

app.MapPost("/api/launch-bulk", async (
    BulkLaunchRequest request,
    ISessionRepository sessionRepo,
    ISessionOverridesRepository overridesRepo,
    IWorkspaceReader workspaceReader,
    INarniaSettingsRepository settingsRepo,
    CancellationToken ct) =>
{
    if (request.SessionIds is not { Length: > 0 })
        return Results.BadRequest("No session IDs provided");
    if (request.SessionIds.Length > 20)
        return Results.BadRequest("Maximum 20 sessions per bulk launch");

    var shellPath = await settingsRepo.GetAsync("shell_path", ct) ?? DetectDefaultShell();
    if (string.IsNullOrWhiteSpace(shellPath))
        return Results.BadRequest("No shell configured. Go to Settings to configure one.");

    var shellName = Path.GetFileNameWithoutExtension(shellPath).ToLowerInvariant();
    var wtPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Microsoft", "WindowsApps", "wt.exe");
    var useWt = OperatingSystem.IsWindows() && File.Exists(wtPath);

    var launched = new List<object>();
    var failed = new List<object>();
    var tabArgs = new List<string>();

    foreach (var sid in request.SessionIds)
    {
        if (!Guid.TryParse(sid, out _))
        {
            failed.Add(new { sessionId = sid, reason = "Invalid session ID format" });
            continue;
        }

        var session = await sessionRepo.GetByIdAsync(sid, ct);
        if (session is null)
        {
            failed.Add(new { sessionId = sid, reason = "Session not found" });
            continue;
        }

        var ov = await overridesRepo.GetOverrideAsync(sid, ct);
        var workspace = workspaceReader.ReadWorkspace(sid);

        // "bestAvailable" resolution: localPath → cwd → gitRoot
        string? directory = null;
        if (ov?.LocalPath is not null && Directory.Exists(ov.LocalPath))
            directory = ov.LocalPath;
        else if (session.Cwd is not null && Directory.Exists(session.Cwd))
            directory = session.Cwd;
        else
        {
            var gitRoot = workspace?.GitRoot ?? session.GitRoot;
            if (gitRoot is not null && Directory.Exists(gitRoot))
                directory = gitRoot;
        }

        var title = ov?.TerminalTitle ?? session.Summary ?? $"Narnia: {sid[..8]}";
        var resumeCmd = $"copilot --resume={sid}";

        if (useWt)
        {
            var safeTitle = title.Replace("\"", "\\\"");
            var dirArg = directory is not null ? $"--startingDirectory \"{directory}\" " : "";
            tabArgs.Add($"new-tab --title \"{safeTitle}\" --suppressApplicationTitle {dirArg}-- \"{shellPath}\" {BuildShellArgs(shellName, resumeCmd)}");
            launched.Add(new { sessionId = sid, title });
        }
        else
        {
            // Non-WT fallback: launch each as a separate process
            var psi = new ProcessStartInfo { FileName = shellPath, UseShellExecute = true };
            if (directory is not null) psi.WorkingDirectory = directory;
            var safeTitle = title.Replace("\"", "\\\"");
            psi.Arguments = shellName switch
            {
                "pwsh" or "powershell" => $"-NoExit -Command \"$host.UI.RawUI.WindowTitle = '{title.Replace("'", "''")}'; {resumeCmd}\"",
                "cmd" => $"/k title {title} & {resumeCmd}",
                _ => $"-c \"printf '\\033]0;{safeTitle}\\007'; {resumeCmd}; exec $SHELL\"",
            };
            try
            {
                Process.Start(psi);
                launched.Add(new { sessionId = sid, title });
            }
            catch (Exception ex)
            {
                failed.Add(new { sessionId = sid, reason = ex.Message });
            }
        }
    }

    // For WT: launch all tabs in a single command
    if (useWt && tabArgs.Count > 0)
    {
        var psi = new ProcessStartInfo
        {
            FileName = wtPath,
            Arguments = string.Join(" ; ", tabArgs),
            UseShellExecute = true,
        };
        try
        {
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            // If the whole WT command fails, move all to failed
            failed.AddRange(launched.Select(l => new { sessionId = ((dynamic)l).sessionId, reason = ex.Message }));
            launched.Clear();
        }
    }

    return Results.Ok(new { launched, failed });
});

app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    pid = Environment.ProcessId,
    version = serverVersion,
}));

app.MapPost("/shutdown", (HttpContext context, IHostApplicationLifetime lifetime) =>
{
    var remoteIp = context.Connection.RemoteIpAddress;
    if (remoteIp is null || !IPAddress.IsLoopback(remoteIp))
    {
        return Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    lifetime.StopApplication();
    return Results.Ok(new { stopping = true });
});

app.Run();
return;

static string? DetectDefaultShell()
{
    // Try pwsh (PowerShell 7+) first
    try
    {
        var psi = new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "where" : "which",
            Arguments = "pwsh",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var proc = Process.Start(psi);
        if (proc is not null)
        {
            var output = proc.StandardOutput.ReadLine();
            proc.WaitForExit(5000);
            if (proc.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
                return output.Trim();
        }
    }
    catch { /* swallow — shell detection is best-effort */ }

    // Fall back to Windows PowerShell
    if (OperatingSystem.IsWindows())
    {
        var ps = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe");
        if (File.Exists(ps)) return ps;
    }

    return null;
}

static string BuildShellArgs(string shellName, string resumeCmd) => shellName switch
{
    "pwsh" or "powershell" => $"-NoExit -Command \"{resumeCmd}\"",
    "cmd" => $"/k {resumeCmd}",
    _ => $"-c \"{resumeCmd}; exec $SHELL\"",
};

internal sealed record OverrideRequest(
    string? DisplayName,
    string? Repository,
    string? Branch,
    string? Notes,
    string? LocalPath,
    string? TerminalTitle);

internal sealed record ArchiveRequest(bool Archived);

internal sealed record SettingRequest(string Key, string? Value);

internal sealed record LaunchRequest(string SessionId, string Target);

internal sealed record BulkLaunchRequest(string[] SessionIds);
