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

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    // Pin the content root to the binary's own directory so wwwroot (and therefore static
    // assets) resolve no matter what working directory the server is launched from. The
    // server is started detached by hooks/skills whose cwd is arbitrary; without this,
    // UseStaticFiles serves from the launcher's cwd and every asset 404s.
    ContentRootPath = AppContext.BaseDirectory,
});

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
builder.Services.AddSingleton<SettingsDatabaseRelocator>();
builder.Services.AddSingleton<SessionService>();
builder.Services.AddSingleton<IWorkspaceReader, WorkspaceReader>();
builder.Services.AddSingleton<ICopilotSessionLockResolver, CopilotSessionLockResolver>();
builder.Services.AddSingleton<SqliteNarniaSettingsRepository>();
builder.Services.AddSingleton<INarniaSettingsRepository>(sp => sp.GetRequiredService<SqliteNarniaSettingsRepository>());
builder.Services.AddSingleton<SqliteTerminalWindowsRepository>();
builder.Services.AddSingleton<ITerminalWindowsRepository>(sp => sp.GetRequiredService<SqliteTerminalWindowsRepository>());
builder.Services.AddSingleton<SqliteSessionGroupsRepository>();
builder.Services.AddSingleton<ISessionGroupsRepository>(sp => sp.GetRequiredService<SqliteSessionGroupsRepository>());
builder.Services.AddSingleton<SqliteScheduledJobRegistry>();
builder.Services.AddSingleton<IScheduledJobRegistry>(sp => sp.GetRequiredService<SqliteScheduledJobRegistry>());
builder.Services.AddSingleton<IScheduledJobWorkspace, ScheduledJobWorkspace>();
builder.Services.AddSingleton<IScheduledJobService, ScheduledJobService>();
builder.Services.AddSingleton<ITerminalCommandBuilder, TerminalCommandBuilder>();
builder.Services.AddSingleton<IProcessLauncher, ShellExecuteProcessLauncher>();
builder.Services.AddSingleton<ITerminalLauncher, TerminalLauncher>();

// Recovery-console window sources. The live snapshotter is the built-in source; additional
// sources (e.g. a future launch-history source) can be registered here and will surface in the
// console automatically via the aggregator.
builder.Services.AddSingleton<ITerminalWindowSource, LiveTerminalWindowSource>();
builder.Services.AddSingleton<ITerminalWindowAggregator, TerminalWindowAggregator>();

if (OperatingSystem.IsWindows())
    builder.Services.AddSingleton<ILogonAutostartManager, WindowsLogonAutostartManager>();
else
    builder.Services.AddSingleton<ILogonAutostartManager, UnsupportedLogonAutostartManager>();

if (OperatingSystem.IsWindows())
    builder.Services.AddSingleton<IScheduledTaskProvider, WindowsScheduledTaskProvider>();
else
    builder.Services.AddSingleton<IScheduledTaskProvider, UnsupportedScheduledTaskProvider>();

if (OperatingSystem.IsWindows())
    builder.Services.AddSingleton<IScheduledTaskRegistrar, WindowsScheduledTaskRegistrar>();
else
    builder.Services.AddSingleton<IScheduledTaskRegistrar, UnsupportedScheduledTaskRegistrar>();

if (OperatingSystem.IsWindows())
    builder.Services.AddSingleton<IPowerShellHostResolver, WindowsPowerShellHostResolver>();
else
    builder.Services.AddSingleton<IPowerShellHostResolver, DefaultPowerShellHostResolver>();

if (OperatingSystem.IsWindows())
{
    builder.Services.AddSingleton<IProcessSnapshotProvider, WmiProcessSnapshotProvider>();
    builder.Services.AddSingleton<ILiveWindowDetector, LiveWindowDetector>();
    builder.Services.AddSingleton<ITerminalWindowSnapshotter, TerminalWindowSnapshotter>();
    builder.Services.AddHostedService<WindowSnapshotterHostedService>();
}

builder.Services.AddRazorComponents();

builder.Services
    .AddMcpServer()
    .WithHttpTransport(httpOptions => httpOptions.Stateless = true)
    .WithTools<SessionTools>()
    .WithTools<ScheduleTools>();

var app = builder.Build();

app.Services.GetRequiredService<SettingsDatabaseRelocator>().RelocateIfNeeded();
app.Services.GetRequiredService<NarniaSettingsDbMigrator>().MigrateUp();

// Headless one-shot: `NexusLabs.Narnia.Web snapshot` records open terminal windows once and
// exits, without starting the web server. Intended for a scheduled task that keeps recording
// even when the server is not running.
if (args.Contains("snapshot", StringComparer.OrdinalIgnoreCase))
{
    if (OperatingSystem.IsWindows())
    {
        var snapshotter = app.Services.GetRequiredService<ITerminalWindowSnapshotter>();
        var retention = app.Services.GetRequiredService<NarniaOptions>().SnapshotterRetentionCount;
        await snapshotter.SnapshotAsync(DateTimeOffset.UtcNow, retention);
    }

    return;
}

var serverVersion = Assembly.GetExecutingAssembly()
    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
    ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString();

// The run-state file is global per-user state owned by a real running server. Integration
// tests boot the app under the "Testing" environment and must not write or delete it.
if (!app.Environment.IsEnvironment("Testing"))
{
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
}

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

// The command that invokes Copilot. Overridable via the "copilot_command" setting for machines
// where a wrapper is required (e.g. Microsoft's "Agency" tooling requires "agency copilot" instead
// of a bare "copilot").
const string DefaultCopilotCommand = "copilot";

// ── Launch API ──────────────────────────────────────────────────────────────
app.MapPost("/api/launch", async (
    LaunchRequest request,
    ISessionRepository sessionRepo,
    ISessionOverridesRepository overridesRepo,
    IWorkspaceReader workspaceReader,
    INarniaSettingsRepository settingsRepo,
    ITerminalLauncher launcher,
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
    var copilotCommand = await settingsRepo.GetAsync("copilot_command", ct) ?? DefaultCopilotCommand;

    var title = ov?.TerminalTitle ?? session.Summary ?? $"Narnia: {ShortSession(request.SessionId)}";
    var shellName = Path.GetFileNameWithoutExtension(shellPath).ToLowerInvariant();
    var tab = new TerminalLaunchTab(request.SessionId, title, directory);

    // A single session opens in its own window; with one tab the mode is moot, but SeparateWindows
    // keeps the semantics explicit and consistent with the unified launcher.
    var outcome = launcher.Launch(shellPath, shellName, [tab], TerminalWindowMode.SeparateWindows, copilotCommand);
    return outcome.Failures.Count == 0
        ? Results.Ok(new { launched = true })
        : Results.BadRequest($"Failed to launch shell: {outcome.Failures[0].Reason}");
});

app.MapPost("/api/launch-bulk", async (
    BulkLaunchRequest request,
    ISessionRepository sessionRepo,
    ISessionOverridesRepository overridesRepo,
    IWorkspaceReader workspaceReader,
    INarniaSettingsRepository settingsRepo,
    ITerminalLauncher launcher,
    CancellationToken ct) =>
{
    if (request.SessionIds is not { Length: > 0 })
        return Results.BadRequest("No session IDs provided");
    if (request.SessionIds.Length > 20)
        return Results.BadRequest("Maximum 20 sessions per bulk launch");

    var shellPath = await settingsRepo.GetAsync("shell_path", ct) ?? DetectDefaultShell();
    if (string.IsNullOrWhiteSpace(shellPath))
        return Results.BadRequest("No shell configured. Go to Settings to configure one.");
    var copilotCommand = await settingsRepo.GetAsync("copilot_command", ct) ?? DefaultCopilotCommand;

    var shellName = Path.GetFileNameWithoutExtension(shellPath).ToLowerInvariant();

    var tabs = new List<TerminalLaunchTab>();
    var failed = new List<object>();

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

        var title = ov?.TerminalTitle ?? session.Summary ?? $"Narnia: {ShortSession(sid)}";
        tabs.Add(new TerminalLaunchTab(sid, title, directory));
    }

    var mode = request.SeparateWindows
        ? TerminalWindowMode.SeparateWindows
        : TerminalWindowMode.SingleWindow;
    var outcome = launcher.Launch(shellPath, shellName, tabs, mode, copilotCommand);

    var launched = outcome.LaunchedSessionIds.Select(id => new { sessionId = id }).ToList();
    failed.AddRange(outcome.Failures.Select(f => new { sessionId = f.SessionId, reason = f.Reason }));

    return Results.Ok(new { launched, failed });
});

// ── Terminal windows (recovery console) API ─────────────────────────────────
app.MapGet("/api/windows", async (
    ITerminalWindowAggregator windows,
    ISessionRepository sessionRepo,
    CancellationToken ct) =>
{
    var snapshot = await windows.GetWindowsAsync(50, ct);

    async Task<object> ProjectAsync(TerminalWindow window)
    {
        var tabs = new List<object>(window.Tabs.Count);
        foreach (var tab in window.Tabs)
        {
            var session = await sessionRepo.GetByIdAsync(tab.SessionId, ct);
            tabs.Add(new
            {
                sessionId = tab.SessionId,
                order = tab.TabOrder,
                directory = tab.Directory,
                summary = session?.Summary,
                repository = session?.Repository,
                branch = session?.Branch,
            });
        }

        return new
        {
            id = window.Id,
            name = window.Name,
            pinned = window.Pinned,
            status = window.Status.ToString().ToLowerInvariant(),
            terminalPid = window.TerminalProcessId,
            occurrenceCount = window.OccurrenceCount,
            firstSeenAt = window.FirstSeenAt,
            lastSeenAt = window.LastSeenAt,
            closedAt = window.ClosedAt,
            tabs,
        };
    }

    var openProjected = new List<object>(snapshot.Open.Count);
    foreach (var window in snapshot.Open)
        openProjected.Add(await ProjectAsync(window));

    var closedProjected = new List<object>(snapshot.Closed.Count);
    foreach (var window in snapshot.Closed)
        closedProjected.Add(await ProjectAsync(window));

    return Results.Ok(new { open = openProjected, closed = closedProjected });
});

app.MapPost("/api/windows/{id}/reopen", async (
    string id,
    ISessionRepository sessionRepo,
    ISessionOverridesRepository overridesRepo,
    IWorkspaceReader workspaceReader,
    INarniaSettingsRepository settingsRepo,
    ITerminalWindowsRepository windowsRepo,
    ITerminalLauncher launcher,
    CancellationToken ct) =>
{
    var window = await windowsRepo.GetByIdAsync(id, ct);
    if (window is null)
        return Results.NotFound("Window not found");
    if (window.Tabs.Count == 0)
        return Results.BadRequest("Window has no tabs to reopen");

    var shellPath = await settingsRepo.GetAsync("shell_path", ct) ?? DetectDefaultShell();
    if (string.IsNullOrWhiteSpace(shellPath))
        return Results.BadRequest("No shell configured. Go to Settings to configure one.");
    var copilotCommand = await settingsRepo.GetAsync("copilot_command", ct) ?? DefaultCopilotCommand;
    var shellName = Path.GetFileNameWithoutExtension(shellPath).ToLowerInvariant();

    var launchTabs = await BuildReopenTabsAsync(window, sessionRepo, overridesRepo, workspaceReader, ct);

    var outcome = launcher.Launch(shellPath, shellName, launchTabs, TerminalWindowMode.SingleWindow, copilotCommand);
    return outcome.Failures.Count == 0
        ? Results.Ok(new { reopened = true, tabs = outcome.LaunchedSessionIds.Count })
        : Results.BadRequest($"Failed to reopen window: {outcome.Failures[0].Reason}");
});

app.MapPost("/api/windows/reopen", async (
    BulkReopenRequest request,
    ISessionRepository sessionRepo,
    ISessionOverridesRepository overridesRepo,
    IWorkspaceReader workspaceReader,
    INarniaSettingsRepository settingsRepo,
    ITerminalWindowsRepository windowsRepo,
    ITerminalLauncher launcher,
    CancellationToken ct) =>
{
    if (request.Ids is not { Length: > 0 })
        return Results.BadRequest("No sessions selected");

    var shellPath = await settingsRepo.GetAsync("shell_path", ct) ?? DetectDefaultShell();
    if (string.IsNullOrWhiteSpace(shellPath))
        return Results.BadRequest("No shell configured. Go to Settings to configure one.");
    var copilotCommand = await settingsRepo.GetAsync("copilot_command", ct) ?? DefaultCopilotCommand;
    var shellName = Path.GetFileNameWithoutExtension(shellPath).ToLowerInvariant();

    var launchTabs = new List<TerminalLaunchTab>();
    var notFound = new List<string>();
    foreach (var id in request.Ids)
    {
        var window = await windowsRepo.GetByIdAsync(id, ct);
        if (window is null || window.Tabs.Count == 0)
        {
            notFound.Add(id);
            continue;
        }

        launchTabs.AddRange(await BuildReopenTabsAsync(window, sessionRepo, overridesRepo, workspaceReader, ct));
    }

    if (launchTabs.Count == 0)
        return Results.BadRequest("No reopenable sessions in the selection.");

    // SeparateWindows opens each session in its own window (the original "reopen all" behavior);
    // otherwise all selected sessions open as tabs in a single window.
    var mode = request.SeparateWindows
        ? TerminalWindowMode.SeparateWindows
        : TerminalWindowMode.SingleWindow;
    var outcome = launcher.Launch(shellPath, shellName, launchTabs, mode, copilotCommand);

    return Results.Ok(new
    {
        reopened = outcome.LaunchedSessionIds.Count,
        failed = outcome.Failures.Select(f => new { sessionId = f.SessionId, reason = f.Reason }).ToList(),
        notFound,
    });
});

app.MapPost("/api/windows/{id}/name", async (
    string id,
    WindowNameRequest request,
    ITerminalWindowsRepository windowsRepo,
    CancellationToken ct) =>
{
    var name = string.IsNullOrWhiteSpace(request.Name) ? null : request.Name.Trim();
    // Naming a window pins it against retention pruning unless the caller says otherwise.
    var pinned = request.Pinned ?? name is not null;
    await windowsRepo.SetNameAsync(id, name, pinned, ct);
    return Results.Ok(new { id, name, pinned });
});

app.MapDelete("/api/windows/{id}", async (
    string id,
    ITerminalWindowsRepository windowsRepo,
    CancellationToken ct) =>
{
    await windowsRepo.DeleteAsync(id, ct);
    return Results.NoContent();
});

// ── Session groups API ──────────────────────────────────────────────────────
app.MapGet("/api/groups", async (
    ISessionGroupsRepository groupsRepo,
    ISessionRepository sessionRepo,
    CancellationToken ct) =>
{
    var groups = await groupsRepo.GetAllAsync(ct);

    var projected = new List<object>(groups.Count);
    foreach (var group in groups)
    {
        var members = new List<object>(group.Members.Count);
        foreach (var member in group.Members)
        {
            var session = await sessionRepo.GetByIdAsync(member.SessionId, ct);
            members.Add(new
            {
                sessionId = member.SessionId,
                order = member.MemberOrder,
                summary = session?.Summary,
                repository = session?.Repository,
                branch = session?.Branch,
            });
        }

        projected.Add(new
        {
            id = group.Id,
            name = group.Name,
            createdAt = group.CreatedAt,
            updatedAt = group.UpdatedAt,
            members,
        });
    }

    return Results.Ok(new { groups = projected });
});

app.MapPost("/api/groups", async (
    GroupRequest request,
    ISessionGroupsRepository groupsRepo,
    CancellationToken ct) =>
{
    var name = request.Name?.Trim();
    if (string.IsNullOrWhiteSpace(name))
        return Results.BadRequest("A group name is required.");
    if (request.SessionIds is not { Length: > 0 })
        return Results.BadRequest("Select at least one session for the group.");

    var group = await groupsRepo.CreateAsync(name, request.SessionIds, DateTimeOffset.UtcNow, ct);
    return Results.Ok(new { id = group.Id, name = group.Name, count = group.Members.Count });
});

app.MapPost("/api/groups/{id}/rename", async (
    string id,
    GroupRenameRequest request,
    ISessionGroupsRepository groupsRepo,
    CancellationToken ct) =>
{
    var name = request.Name?.Trim();
    if (string.IsNullOrWhiteSpace(name))
        return Results.BadRequest("A group name is required.");

    var group = await groupsRepo.GetByIdAsync(id, ct);
    if (group is null)
        return Results.NotFound("Group not found");

    await groupsRepo.RenameAsync(id, name, DateTimeOffset.UtcNow, ct);
    return Results.Ok(new { id, name });
});

app.MapPost("/api/groups/{id}/members", async (
    string id,
    GroupMembersRequest request,
    ISessionGroupsRepository groupsRepo,
    CancellationToken ct) =>
{
    if (request.SessionIds is not { Length: > 0 })
        return Results.BadRequest("Select at least one session for the group.");

    var group = await groupsRepo.GetByIdAsync(id, ct);
    if (group is null)
        return Results.NotFound("Group not found");

    await groupsRepo.SetMembersAsync(id, request.SessionIds, DateTimeOffset.UtcNow, ct);
    return Results.Ok(new { id, count = request.SessionIds.Length });
});

app.MapDelete("/api/groups/{id}", async (
    string id,
    ISessionGroupsRepository groupsRepo,
    CancellationToken ct) =>
{
    await groupsRepo.DeleteAsync(id, ct);
    return Results.NoContent();
});

app.MapPost("/api/groups/{id}/reopen", async (
    string id,
    GroupReopenRequest request,
    ISessionGroupsRepository groupsRepo,
    ISessionRepository sessionRepo,
    ISessionOverridesRepository overridesRepo,
    IWorkspaceReader workspaceReader,
    INarniaSettingsRepository settingsRepo,
    ITerminalLauncher launcher,
    CancellationToken ct) =>
{
    var group = await groupsRepo.GetByIdAsync(id, ct);
    if (group is null)
        return Results.NotFound("Group not found");
    if (group.Members.Count == 0)
        return Results.BadRequest("Group has no sessions to reopen.");

    var shellPath = await settingsRepo.GetAsync("shell_path", ct) ?? DetectDefaultShell();
    if (string.IsNullOrWhiteSpace(shellPath))
        return Results.BadRequest("No shell configured. Go to Settings to configure one.");
    var copilotCommand = await settingsRepo.GetAsync("copilot_command", ct) ?? DefaultCopilotCommand;
    var shellName = Path.GetFileNameWithoutExtension(shellPath).ToLowerInvariant();

    var launchTabs = new List<TerminalLaunchTab>(group.Members.Count);
    foreach (var member in group.Members.OrderBy(m => m.MemberOrder))
    {
        launchTabs.Add(await BuildLaunchTabAsync(
            member.SessionId, null, sessionRepo, overridesRepo, workspaceReader, ct));
    }

    // SeparateWindows opens each session in its own window; otherwise the whole group opens as
    // tabs in a single window. Reusing the unified launcher keeps this identical to every other
    // launch path.
    var mode = request.SeparateWindows
        ? TerminalWindowMode.SeparateWindows
        : TerminalWindowMode.SingleWindow;
    var outcome = launcher.Launch(shellPath, shellName, launchTabs, mode, copilotCommand);

    return Results.Ok(new
    {
        reopened = outcome.LaunchedSessionIds.Count,
        failed = outcome.Failures.Select(f => new { sessionId = f.SessionId, reason = f.Reason }).ToList(),
    });
});

// ── Scheduled jobs registry API ─────────────────────────────────────────────
app.MapGet("/api/schedules", async (
    IScheduledJobService jobService,
    CancellationToken ct) =>
{
    var view = await jobService.ListAsync(ct);

    var projectedJobs = view.Jobs.Select(v => (object)new
    {
        id = v.Job.Id,
        name = v.Job.Name,
        description = v.Job.Description,
        cwd = v.Job.Cwd,
        cadence = v.Job.Cadence,
        args = v.Job.Args,
        scriptPath = v.Job.ScriptPath,
        logDir = v.Job.LogDir,
        allowFlags = v.Job.AllowFlags,
        taskFolder = v.Job.TaskFolder,
        taskName = v.Job.TaskName,
        notes = v.Job.Notes,
        createdAt = v.Job.CreatedAt,
        updatedAt = v.Job.UpdatedAt,
        prompt = v.Job.Prompt,
        cadenceKind = v.Job.CadenceKind,
        cadenceTime = v.Job.CadenceTime,
        cadenceDays = v.Job.CadenceDays,
        copilotArgs = v.Job.CopilotArgs,
        skills = v.Job.Skills.Select(s => new
        {
            skill = s.Skill,
            resolution = s.Resolution.ToString().ToLowerInvariant(),
        }),
        status = v.Status is null ? null : ProjectStatus(v.Status),
        taskFound = v.TaskFound,
    }).ToList();

    var untracked = view.Untracked.Select(ProjectStatus).ToList();

    return Results.Ok(new
    {
        schedulerSupported = view.SchedulerSupported,
        jobs = projectedJobs,
        untracked,
    });

    static object ProjectStatus(ScheduledTaskStatus s) => new
    {
        taskFolder = s.TaskFolder,
        taskName = s.TaskName,
        state = s.State.ToString().ToLowerInvariant(),
        lastRunTime = s.LastRunTime,
        lastResult = s.LastResult,
        nextRunTime = s.NextRunTime,
        actionSummary = s.ActionSummary,
    };
});

app.MapPost("/api/schedules", async (
    ScheduleCreateRequest request,
    IScheduledJobService jobService,
    CancellationToken ct) =>
{
    var result = await jobService.CreateAsync(request.ToInput(), request.Register, ct);
    if (!result.Ok)
        return Results.BadRequest(result.Error);

    return result.Registered
        ? Results.Ok(new { registered = true, id = result.Job!.Id })
        : Results.Ok(new { registered = false, script = result.Script, command = result.Command });
});

app.MapPut("/api/schedules/{id}", async (
    string id,
    ScheduleCreateRequest request,
    IScheduledJobService jobService,
    CancellationToken ct) =>
{
    var result = await jobService.UpdateAsync(id, request.ToInput(), ct);
    if (result.NotFound)
        return Results.NotFound(result.Error);

    return result.Ok
        ? Results.Ok(new { id })
        : Results.BadRequest(result.Error);
});

app.MapPost("/api/schedules/{id}/enable", async (
    string id, ScheduleEnableRequest request, IScheduledJobService jobService, CancellationToken ct) =>
{
    var result = await jobService.SetEnabledAsync(id, request.Enabled, ct);
    if (result.NotFound) return Results.NotFound(result.Error);
    return result.Ok ? Results.Ok(new { enabled = request.Enabled }) : Results.BadRequest(result.Error);
});

app.MapPost("/api/schedules/{id}/run", async (
    string id, IScheduledJobService jobService, CancellationToken ct) =>
{
    var result = await jobService.RunAsync(id, ct);
    if (result.NotFound) return Results.NotFound(result.Error);
    return result.Ok ? Results.Ok(new { started = true }) : Results.BadRequest(result.Error);
});

app.MapDelete("/api/schedules/{id}", async (
    string id, IScheduledJobService jobService, CancellationToken ct) =>
{
    var result = await jobService.DeleteAsync(id, ct);
    return result.NotFound ? Results.NotFound(result.Error) : Results.NoContent();
});

app.MapGet("/api/schedules/{id}/log", async (
    string id, IScheduledJobService jobService, CancellationToken ct) =>
{
    var log = await jobService.GetLatestLogAsync(id, ct);
    if (log.JobNotFound)
        return Results.NotFound("Job not found");

    return Results.Ok(new { found = log.Found, path = log.Path, content = log.Content, truncated = log.Truncated, isRunning = log.IsRunning });
});

// ── Logon autostart API ─────────────────────────────────────────────────────
app.MapGet("/api/autostart", (ILogonAutostartManager autostart) =>
    Results.Ok(new
    {
        supported = autostart.IsSupported,
        enabled = autostart.IsSupported && autostart.IsEnabled(),
    }));

app.MapPost("/api/autostart", (AutostartRequest request, ILogonAutostartManager autostart) =>
{
    if (!autostart.IsSupported)
        return Results.BadRequest("Logon autostart is only supported on Windows.");

    if (request.Enabled)
        autostart.Enable();
    else
        autostart.Disable();

    return Results.Ok(new { enabled = autostart.IsEnabled() });
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

// A short, display-only session label. Real session ids are GUIDs, but guard against shorter
// ids so a malformed value never crashes a launch/reopen title fallback.
static string ShortSession(string sessionId) =>
    sessionId.Length >= 8 ? sessionId[..8] : sessionId;


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

static string? ResolveReopenDirectory(
    string? capturedDirectory,
    SessionOverride? ov,
    Session? session,
    WorkspaceInfo? workspace)
{
    if (!string.IsNullOrWhiteSpace(capturedDirectory) && Directory.Exists(capturedDirectory))
        return capturedDirectory;
    if (ov?.LocalPath is not null && Directory.Exists(ov.LocalPath))
        return ov.LocalPath;
    if (session?.Cwd is not null && Directory.Exists(session.Cwd))
        return session.Cwd;

    var gitRoot = workspace?.GitRoot ?? session?.GitRoot;
    if (gitRoot is not null && Directory.Exists(gitRoot))
        return gitRoot;

    return null;
}

// Resolves a recorded window's tabs into launch tabs (directory + title per session), shared by the
// single-window reopen and the multi-select reopen so both resolve identically.
static async Task<List<TerminalLaunchTab>> BuildReopenTabsAsync(
    TerminalWindow window,
    ISessionRepository sessionRepo,
    ISessionOverridesRepository overridesRepo,
    IWorkspaceReader workspaceReader,
    CancellationToken ct)
{
    var launchTabs = new List<TerminalLaunchTab>(window.Tabs.Count);
    foreach (var tab in window.Tabs.OrderBy(t => t.TabOrder))
    {
        launchTabs.Add(await BuildLaunchTabAsync(
            tab.SessionId, tab.Directory, sessionRepo, overridesRepo, workspaceReader, ct));
    }

    return launchTabs;
}

// Resolves a single session id into a launch tab (directory + title), shared by window reopen and
// session-group reopen. A captured directory (when a window recorded one) takes precedence;
// otherwise the directory falls back to the session override's local path, the session cwd, then
// the git root.
static async Task<TerminalLaunchTab> BuildLaunchTabAsync(
    string sessionId,
    string? capturedDirectory,
    ISessionRepository sessionRepo,
    ISessionOverridesRepository overridesRepo,
    IWorkspaceReader workspaceReader,
    CancellationToken ct)
{
    var session = await sessionRepo.GetByIdAsync(sessionId, ct);
    var ov = await overridesRepo.GetOverrideAsync(sessionId, ct);
    var workspace = workspaceReader.ReadWorkspace(sessionId);
    var directory = ResolveReopenDirectory(capturedDirectory, ov, session, workspace);
    var title = ov?.TerminalTitle ?? session?.Summary ?? $"Narnia: {ShortSession(sessionId)}";
    return new TerminalLaunchTab(sessionId, title, directory);
}

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

internal sealed record BulkLaunchRequest(string[] SessionIds, bool SeparateWindows = false);
internal sealed record BulkReopenRequest(string[] Ids, bool SeparateWindows = false);

internal sealed record WindowNameRequest(string? Name, bool? Pinned);

internal sealed record GroupRequest(string? Name, string[] SessionIds);
internal sealed record GroupRenameRequest(string? Name);
internal sealed record GroupMembersRequest(string[] SessionIds);
internal sealed record GroupReopenRequest(bool SeparateWindows = false);

internal sealed record ScheduleCreateRequest(
    string Name,
    string? Description,
    string? Cwd,
    string? Prompt,
    string? AllowFlags,
    string? CopilotArgs,
    string? Execute,
    string? Args,
    string? ScriptPath,
    string? LogDir,
    string? TaskName,
    string? CadenceKind,
    string? Time,
    string[]? Days,
    int? DayOfMonth,
    ScheduleSkillDto[]? Skills,
    bool Register = false);

internal sealed record ScheduleSkillDto(string Skill, string? Resolution);

/// <summary>Maps the HTTP create/update request onto the storage-agnostic service input.</summary>
internal static class ScheduleRequestMapping
{
    public static ScheduledJobInput ToInput(this ScheduleCreateRequest r) => new(
        Name: r.Name,
        Description: r.Description,
        Cwd: r.Cwd,
        Prompt: r.Prompt,
        AllowFlags: r.AllowFlags,
        CopilotArgs: r.CopilotArgs,
        TaskName: r.TaskName,
        CadenceKind: r.CadenceKind,
        Time: r.Time,
        Days: r.Days,
        DayOfMonth: r.DayOfMonth,
        Skills: r.Skills?.Select(s => new ScheduledJobSkillInput(s.Skill, s.Resolution)).ToList());
}

internal sealed record ScheduleEnableRequest(bool Enabled);

internal sealed record AutostartRequest(bool Enabled);

/// <summary>
/// Exposes the implicit top-level <c>Program</c> class so integration tests can boot the app
/// with <see cref="Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory{TEntryPoint}"/>.
/// </summary>
public partial class Program;
