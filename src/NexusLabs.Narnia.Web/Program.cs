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
builder.Services.AddSingleton<SqliteNarniaSettingsRepository>();
builder.Services.AddSingleton<INarniaSettingsRepository>(sp => sp.GetRequiredService<SqliteNarniaSettingsRepository>());
builder.Services.AddSingleton<SqliteTerminalWindowsRepository>();
builder.Services.AddSingleton<ITerminalWindowsRepository>(sp => sp.GetRequiredService<SqliteTerminalWindowsRepository>());
builder.Services.AddSingleton<SqliteSessionGroupsRepository>();
builder.Services.AddSingleton<ISessionGroupsRepository>(sp => sp.GetRequiredService<SqliteSessionGroupsRepository>());
builder.Services.AddSingleton<SqliteScheduledJobRegistry>();
builder.Services.AddSingleton<IScheduledJobRegistry>(sp => sp.GetRequiredService<SqliteScheduledJobRegistry>());
builder.Services.AddSingleton<IScheduledJobWorkspace, ScheduledJobWorkspace>();
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
    .WithTools<SessionTools>();

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

    var title = ov?.TerminalTitle ?? session.Summary ?? $"Narnia: {ShortSession(request.SessionId)}";
    var shellName = Path.GetFileNameWithoutExtension(shellPath).ToLowerInvariant();
    var tab = new TerminalLaunchTab(request.SessionId, title, directory);

    // A single session opens in its own window; with one tab the mode is moot, but SeparateWindows
    // keeps the semantics explicit and consistent with the unified launcher.
    var outcome = launcher.Launch(shellPath, shellName, [tab], TerminalWindowMode.SeparateWindows);
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
    var outcome = launcher.Launch(shellPath, shellName, tabs, mode);

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
    var shellName = Path.GetFileNameWithoutExtension(shellPath).ToLowerInvariant();

    var launchTabs = await BuildReopenTabsAsync(window, sessionRepo, overridesRepo, workspaceReader, ct);

    var outcome = launcher.Launch(shellPath, shellName, launchTabs, TerminalWindowMode.SingleWindow);
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
    var outcome = launcher.Launch(shellPath, shellName, launchTabs, mode);

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
    var outcome = launcher.Launch(shellPath, shellName, launchTabs, mode);

    return Results.Ok(new
    {
        reopened = outcome.LaunchedSessionIds.Count,
        failed = outcome.Failures.Select(f => new { sessionId = f.SessionId, reason = f.Reason }).ToList(),
    });
});

// ── Scheduled jobs registry API (read-only) ─────────────────────────────────
app.MapGet("/api/schedules", async (
    IScheduledJobRegistry registry,
    IScheduledTaskProvider taskProvider,
    CancellationToken ct) =>
{
    const string narniaFolder = @"\Narnia\";

    var jobs = await registry.GetAllAsync(ct);
    var narniaTasks = await taskProvider.ListInFolderAsync(narniaFolder, ct);

    var tasksByKey = new Dictionary<string, ScheduledTaskStatus>(StringComparer.OrdinalIgnoreCase);
    foreach (var task in narniaTasks)
        tasksByKey[TaskKey(task.TaskFolder, task.TaskName)] = task;

    var matchedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    var projectedJobs = new List<object>(jobs.Count);
    foreach (var job in jobs)
    {
        var key = TaskKey(job.TaskFolder, job.TaskName);
        if (!tasksByKey.TryGetValue(key, out var status))
            status = await taskProvider.GetAsync(job.TaskFolder, job.TaskName, ct);

        if (status is not null)
            matchedKeys.Add(key);

        projectedJobs.Add(new
        {
            id = job.Id,
            name = job.Name,
            description = job.Description,
            cwd = job.Cwd,
            cadence = job.Cadence,
            args = job.Args,
            scriptPath = job.ScriptPath,
            logDir = job.LogDir,
            allowFlags = job.AllowFlags,
            taskFolder = job.TaskFolder,
            taskName = job.TaskName,
            notes = job.Notes,
            createdAt = job.CreatedAt,
            updatedAt = job.UpdatedAt,
            prompt = job.Prompt,
            cadenceKind = job.CadenceKind,
            cadenceTime = job.CadenceTime,
            cadenceDays = job.CadenceDays,
            copilotArgs = job.CopilotArgs,
            skills = job.Skills.Select(s => new
            {
                skill = s.Skill,
                resolution = s.Resolution.ToString().ToLowerInvariant(),
            }),
            status = status is null ? null : ProjectStatus(status),
            taskFound = status is not null,
        });
    }

    var untracked = narniaTasks
        .Where(t => !matchedKeys.Contains(TaskKey(t.TaskFolder, t.TaskName)))
        .Select(ProjectStatus)
        .ToList();

    return Results.Ok(new
    {
        schedulerSupported = taskProvider.IsSupported,
        jobs = projectedJobs,
        untracked,
    });

    static string TaskKey(string folder, string name) =>
        $"{folder.Trim('\\')}|{name}";

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
    IScheduledJobRegistry registry,
    IScheduledTaskRegistrar registrar,
    IScheduledJobWorkspace workspace,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.Name))
        return Results.BadRequest("A job name is required.");
    if (string.IsNullOrWhiteSpace(request.Prompt))
        return Results.BadRequest("A prompt is required (it is what Copilot runs).");

    var jobId = Guid.NewGuid().ToString();
    var cadence = BuildCadence(request);
    var (script, registration) = BuildOwnedJob(jobId, request, cadence, workspace);

    // Copy-paste mode: catalog nothing, just hand back the generated wrapper + registration command.
    if (!request.Register)
        return Results.Ok(new { registered = false, script, command = ScheduledTaskRegistrationScript.Build(registration) });

    if (!registrar.IsSupported)
        return Results.BadRequest("Registering tasks is not supported on this platform. Copy the command instead.");

    await workspace.WriteScriptAsync(jobId, script, ct);
    var draft = BuildOwnedDraft(request, cadence, workspace.ScriptPath(jobId), workspace.LogDirectory(jobId));

    // Create with the pre-chosen id so the workspace folder, marker, and catalog row all agree.
    var job = await registry.CreateWithIdAsync(jobId, draft, DateTimeOffset.UtcNow, ct);
    var outcome = await registrar.RegisterAsync(registration, ct);
    if (!outcome.Ok)
    {
        await registry.DeleteAsync(job.Id, ct);
        workspace.Delete(job.Id);
        return Results.BadRequest($"Task registration failed: {outcome.Error}");
    }

    return Results.Ok(new { registered = true, id = job.Id });
});

app.MapPut("/api/schedules/{id}", async (
    string id,
    ScheduleCreateRequest request,
    IScheduledJobRegistry registry,
    IScheduledTaskRegistrar registrar,
    IScheduledJobWorkspace workspace,
    CancellationToken ct) =>
{
    var existing = await registry.GetByIdAsync(id, ct);
    if (existing is null)
        return Results.NotFound("Job not found");
    if (string.IsNullOrWhiteSpace(request.Name))
        return Results.BadRequest("A job name is required.");
    if (string.IsNullOrWhiteSpace(request.Prompt))
        return Results.BadRequest("A prompt is required.");

    var cadence = BuildCadence(request);
    var (script, registration) = BuildOwnedJob(id, request, cadence, workspace);

    if (!registrar.IsSupported)
        return Results.BadRequest("Editing tasks is not supported on this platform.");

    await workspace.WriteScriptAsync(id, script, ct);
    var draft = BuildOwnedDraft(request, cadence, workspace.ScriptPath(id), workspace.LogDirectory(id));
    await registry.UpdateAsync(id, draft, DateTimeOffset.UtcNow, ct);

    // Register with -Force overwrites the existing task in place (trigger/action refreshed).
    var outcome = await registrar.RegisterAsync(registration, ct);
    return outcome.Ok
        ? Results.Ok(new { id })
        : Results.BadRequest($"Task update failed: {outcome.Error}");
});

app.MapPost("/api/schedules/{id}/enable", async (
    string id, ScheduleEnableRequest request, IScheduledJobRegistry registry,
    IScheduledTaskRegistrar registrar, CancellationToken ct) =>
{
    var job = await registry.GetByIdAsync(id, ct);
    if (job is null) return Results.NotFound("Job not found");
    var r = await registrar.SetEnabledAsync(job.TaskFolder, job.TaskName, request.Enabled, ct);
    return r.Ok ? Results.Ok(new { enabled = request.Enabled }) : Results.BadRequest(r.Error);
});

app.MapPost("/api/schedules/{id}/run", async (
    string id, IScheduledJobRegistry registry, IScheduledTaskRegistrar registrar, CancellationToken ct) =>
{
    var job = await registry.GetByIdAsync(id, ct);
    if (job is null) return Results.NotFound("Job not found");
    var r = await registrar.RunAsync(job.TaskFolder, job.TaskName, ct);
    return r.Ok ? Results.Ok(new { started = true }) : Results.BadRequest(r.Error);
});

app.MapDelete("/api/schedules/{id}", async (
    string id, IScheduledJobRegistry registry,
    IScheduledTaskRegistrar registrar, IScheduledJobWorkspace workspace, CancellationToken ct) =>
{
    var job = await registry.GetByIdAsync(id, ct);
    if (job is null) return Results.NotFound("Job not found");

    // Every job is a first-class Narnia job that owns its scheduled task and generated script, so
    // deleting a job always removes both.
    await registrar.DeleteAsync(job.TaskFolder, job.TaskName, ct);
    workspace.Delete(id);
    await registry.DeleteAsync(id, ct);
    return Results.NoContent();
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

// Maps a create request's cadence fields into the normalized cadence; defaults to a daily 05:00.
static ScheduleCadence BuildCadence(ScheduleCreateRequest request)
{
    var time = TimeOnly.TryParse(request.Time, out var t) ? t : new TimeOnly(5, 0);
    var kind = request.CadenceKind?.ToLowerInvariant() switch
    {
        "weekly" => ScheduleCadenceKind.Weekly,
        "monthly" => ScheduleCadenceKind.Monthly,
        _ => ScheduleCadenceKind.Daily,
    };
    var days = (request.Days ?? [])
        .Select(d => Enum.TryParse<DayOfWeek>(d, ignoreCase: true, out var dow) ? dow : (DayOfWeek?)null)
        .Where(d => d is not null).Select(d => d!.Value).ToList();
    var dayOfMonth = request.DayOfMonth is >= 1 and <= 31 ? request.DayOfMonth.Value : 1;
    return new ScheduleCadence(kind, time, days, dayOfMonth);
}

// Builds the generated wrapper script and the standardized task registration for a Narnia-owned
// job. The task runs pwsh against the generated script under \Narnia\ named after the job.
static (string Script, ScheduledTaskRegistration Registration) BuildOwnedJob(
    string jobId, ScheduleCreateRequest request, ScheduleCadence cadence, IScheduledJobWorkspace workspace)
{
    const string folder = @"\Narnia\";
    var taskName = string.IsNullOrWhiteSpace(request.TaskName) ? request.Name : request.TaskName!;
    var logDir = workspace.LogDirectory(jobId);
    var script = ScheduledJobScript.Build(
        request.Name, request.Prompt ?? "", request.Cwd, request.AllowFlags, request.CopilotArgs, logDir);
    var scriptPath = workspace.ScriptPath(jobId);
    var args = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"";
    var registration = new ScheduledTaskRegistration(
        jobId, folder, taskName, "powershell.exe", args, request.Cwd, cadence);
    return (script, registration);
}

// Builds the catalog draft for a Narnia-owned job, keyed to its generated script + log paths.
static ScheduledJobDraft BuildOwnedDraft(
    ScheduleCreateRequest request, ScheduleCadence cadence, string scriptPath, string logDir)
{
    const string folder = @"\Narnia\";
    var taskName = string.IsNullOrWhiteSpace(request.TaskName) ? request.Name : request.TaskName!;
    var skills = (request.Skills ?? [])
        .Where(s => !string.IsNullOrWhiteSpace(s.Skill))
        .Select((s, i) => new ScheduledJobSkill(
            s.Skill,
            Enum.TryParse<SkillResolution>(s.Resolution, ignoreCase: true, out var r) ? r : SkillResolution.Unknown,
            i))
        .ToList();
    // Weekly stores its day names in cadence_days; monthly reuses the same column for its day number,
    // so both round-trip for edit prefill without a schema change.
    var cadenceDays = cadence.Kind switch
    {
        ScheduleCadenceKind.Weekly => string.Join(",", cadence.DaysOfWeek.Select(d => d.ToString())),
        ScheduleCadenceKind.Monthly => cadence.DayOfMonth.ToString(),
        _ => "",
    };

    return new ScheduledJobDraft(
        Name: request.Name, Description: request.Description, Cwd: request.Cwd, Cadence: cadence.Describe(),
        Args: null, ScriptPath: scriptPath, LogDir: logDir, AllowFlags: request.AllowFlags,
        TaskFolder: folder, TaskName: taskName, Notes: null, Skills: skills,
        Prompt: request.Prompt, CadenceKind: cadence.Kind.ToString(),
        CadenceTime: cadence.TimeOfDay.ToString("HH\\:mm"), CadenceDays: cadenceDays.Length > 0 ? cadenceDays : null,
        CopilotArgs: request.CopilotArgs);
}


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

internal sealed record ScheduleEnableRequest(bool Enabled);

internal sealed record AutostartRequest(bool Enabled);

/// <summary>
/// Exposes the implicit top-level <c>Program</c> class so integration tests can boot the app
/// with <see cref="Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory{TEntryPoint}"/>.
/// </summary>
public partial class Program;
