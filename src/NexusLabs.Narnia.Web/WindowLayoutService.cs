using NexusLabs.Narnia.Core.Models;
using NexusLabs.Narnia.Core.Repositories;
using NexusLabs.Narnia.Core.Services;

namespace NexusLabs.Narnia.Web;

/// <summary>Coordinates Layout capture, Collection launch, HWND discovery, and placement.</summary>
public sealed class WindowLayoutService(
    IWindowLayoutPlatform platform,
    IWorkCollectionsRepository collectionsRepository,
    ISessionRepository sessionsRepository,
    ISessionOverridesRepository overridesRepository,
    IWorkspaceReader workspaceReader,
    INarniaSettingsRepository settingsRepository,
    ITerminalCommandBuilder commandBuilder,
    ITerminalLauncher terminalLauncher,
    ILaunchCollisionDetector collisionDetector,
    ICopilotSessionActivityReader activityReader) : IWindowLayoutService
{
    private static readonly TimeSpan WindowDetectionTimeout = TimeSpan.FromSeconds(20);

    /// <inheritdoc />
    public async ValueTask<WindowLayoutCaptureView> CaptureAsync(CancellationToken ct)
    {
        var snapshot = platform.Capture();
        if (!snapshot.IsAvailable)
        {
            return new WindowLayoutCaptureView(
                false,
                snapshot.UnavailableReason,
                []);
        }

        var collections = await collectionsRepository.GetAllAsync(ct);
        var sessionIds = collections
            .SelectMany(collection => collection.Members)
            .Select(member => member.SessionId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var sessions = await sessionsRepository.GetByIdsAsync(sessionIds, ct);
        var overrides = await overridesRepository.GetAllOverridesAsync(ct);
        var titlesByCollection = collections.ToDictionary(
            collection => collection.Id,
            collection => collection.Members
                .Select(member => EffectiveTitle(member.SessionId, sessions, overrides))
                .Where(title => !string.IsNullOrWhiteSpace(title))
                .Select(title => title!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase),
            StringComparer.Ordinal);

        var candidates = snapshot.Windows
            .Select(window => new WindowLayoutCaptureCandidate(
                window,
                SuggestCollection(window.Title, collections, titlesByCollection)))
            .ToArray();
        return new WindowLayoutCaptureView(true, null, candidates);
    }

    /// <inheritdoc />
    public async ValueTask<WindowLayoutLaunchResult> LaunchAsync(
        WindowLayout layout,
        bool force,
        CancellationToken ct)
    {
        if (!platform.IsSupported)
            return PreflightFailure("Window Layout restore requires Windows.");
        if (commandBuilder.FindWindowsTerminalPath() is null)
            return PreflightFailure("Window Layout restore requires Windows Terminal.");
        if (layout.Slots.Count == 0)
            return PreflightFailure("Layout has no windows to launch.");

        var capture = platform.Capture();
        if (!capture.IsAvailable || capture.Monitors.Count == 0)
        {
            return PreflightFailure(
                capture.UnavailableReason ?? "No desktop monitors are available.");
        }

        var allCollections = await collectionsRepository.GetAllAsync(ct);
        var collectionsById = allCollections.ToDictionary(
            collection => collection.Id,
            StringComparer.Ordinal);
        var issues = new List<string>();
        var slotSources = new List<LayoutSlotSource>();
        foreach (var slot in layout.Slots)
        {
            if (slot.ContentKind == WindowLayoutContentKind.Collection)
            {
                if (slot.CollectionId is null ||
                    !collectionsById.TryGetValue(slot.CollectionId, out var collection))
                {
                    issues.Add(
                        $"The Collection referenced by window '{slot.CapturedWindowTitle ?? slot.Id}' no longer exists.");
                    continue;
                }
                if (collection.Members.Count == 0)
                {
                    issues.Add($"Collection '{collection.Name}' has no sessions.");
                    continue;
                }

                slotSources.Add(new LayoutSlotSource(
                    slot,
                    collection.Name,
                    collection.Members.Select(member => member.SessionId).ToArray()));
            }
            else if (slot.SessionId is null)
            {
                issues.Add(
                    $"The session referenced by window '{slot.CapturedWindowTitle ?? slot.Id}' is invalid.");
            }
            else
            {
                slotSources.Add(new LayoutSlotSource(slot, null, [slot.SessionId]));
            }
        }

        var duplicateSessions = slotSources
            .SelectMany(source => source.SessionIds.Select(sessionId => new
            {
                source.Slot,
                SessionId = sessionId,
            }))
            .GroupBy(item => item.SessionId, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Select(item => item.Slot.Id).Distinct().Count() > 1)
            .ToArray();
        foreach (var duplicate in duplicateSessions)
        {
            issues.Add(
                $"Session {Short(duplicate.Key)} appears in multiple windows in this Layout.");
        }

        var allSessionIds = slotSources
            .SelectMany(source => source.SessionIds)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var activeSessionIds = activityReader.GetActiveSessionIds();
        var activeTargets = allSessionIds
            .Where(activeSessionIds.Contains)
            .ToArray();
        if (activeTargets.Length > 0)
        {
            issues.Add(
                $"{activeTargets.Length} Layout session{(activeTargets.Length == 1 ? " is" : "s are")} already active.");
        }

        var sessions = await sessionsRepository.GetByIdsAsync(allSessionIds, ct);
        foreach (var missingSessionId in allSessionIds.Where(id => !sessions.ContainsKey(id)))
            issues.Add($"Session {Short(missingSessionId)} is unavailable.");

        if (issues.Count > 0)
            return new WindowLayoutLaunchResult(false, issues, [], []);

        var overrides = await overridesRepository.GetAllOverridesAsync(ct);
        var resolvedSlots = slotSources
            .Select(source => source with
            {
                ContentName = source.ContentName ??
                    sessions[source.SessionIds[0]].Summary ??
                    $"Session {Short(source.SessionIds[0])}",
            })
            .ToArray();
        var tabsBySlot = resolvedSlots.ToDictionary(
            source => source.Slot.Id,
            source => source.SessionIds
                .Select(sessionId => BuildTab(
                    sessionId,
                    sessions[sessionId],
                    overrides.TryGetValue(sessionId, out var sessionOverride)
                        ? sessionOverride
                        : null))
                .ToArray(),
            StringComparer.Ordinal);
        var allTabs = tabsBySlot.Values.SelectMany(tabs => tabs).ToArray();
        if (!force)
        {
            var collisions = await collisionDetector.DetectAsync(allTabs, ct);
            if (collisions.Count > 0)
            {
                return new WindowLayoutLaunchResult(
                    false,
                    [],
                    collisions,
                    []);
            }
        }

        var shellPath =
            await settingsRepository.GetAsync("shell_path", ct) ??
            DetectDefaultShell();
        if (string.IsNullOrWhiteSpace(shellPath))
            return PreflightFailure("No shell is configured. Open Settings to choose one.");
        var copilotCommand =
            await settingsRepository.GetAsync(CopilotSettingKeys.Command, ct) ??
            CopilotSettingKeys.DefaultCommand;
        var shellName = Path.GetFileNameWithoutExtension(shellPath).ToLowerInvariant();

        var results = new List<WindowLayoutWindowLaunchResult>(layout.Slots.Count);
        foreach (var source in resolvedSlots
            .OrderByDescending(item => item.Slot.ZOrder)
            .ThenByDescending(item => item.Slot.SlotOrder))
        {
            var tabs = tabsBySlot[source.Slot.Id];
            var existingHandles = platform.Capture().Windows
                .Select(window => window.Handle)
                .ToHashSet();
            var outcome = terminalLauncher.Launch(
                shellPath,
                shellName,
                tabs,
                TerminalWindowMode.NewWindow,
                copilotCommand);
            if (outcome.LaunchedSessionIds.Count == 0)
            {
                results.Add(FailedWindow(
                    source,
                    outcome.Failures,
                    "No session in this Layout window could be launched."));
                continue;
            }

            var window = await platform.WaitForNewTerminalWindowAsync(
                existingHandles,
                tabs.Select(tab => tab.Title).ToArray(),
                WindowDetectionTimeout,
                ct);
            if (window is null)
            {
                results.Add(FailedWindow(
                    source,
                    outcome.Failures,
                    "Windows Terminal did not expose a new window before the timeout."));
                continue;
            }

            var placement = WindowLayoutPlacementResolver.Resolve(
                source.Slot,
                capture.Monitors);
            var applied = platform.ApplyPlacement(window.Handle, placement);
            results.Add(new WindowLayoutWindowLaunchResult(
                source.Slot.Id,
                source.Slot.ContentKind,
                source.Slot.ContentId,
                source.ContentName!,
                applied.Success && outcome.Failures.Count == 0,
                outcome.LaunchedSessionIds.Count,
                window.Handle,
                placement.Adaptation,
                placement.Bounds,
                applied.ActualBounds,
                outcome.Failures,
                applied.Error));
        }

        return new WindowLayoutLaunchResult(true, [], [], results);

        TerminalLaunchTab BuildTab(
            string sessionId,
            Session session,
            SessionOverride? sessionOverride)
        {
            var workspace = workspaceReader.ReadWorkspace(sessionId);
            var directory = ResolveDirectory(sessionOverride, session, workspace);
            var title =
                sessionOverride?.TerminalTitle ??
                session.Summary ??
                $"Narnia: {Short(sessionId)}";
            return new TerminalLaunchTab(sessionId, title, directory);
        }
    }

    private static string? SuggestCollection(
        string windowTitle,
        IReadOnlyList<WorkCollection> collections,
        IReadOnlyDictionary<string, HashSet<string>> titlesByCollection)
    {
        var titleMatches = collections
            .Where(collection =>
                titlesByCollection[collection.Id].Contains(windowTitle))
            .ToArray();
        if (titleMatches.Length == 1)
            return titleMatches[0].Id;

        var nameMatches = collections
            .Where(collection => string.Equals(
                collection.Name,
                windowTitle,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return nameMatches.Length == 1 ? nameMatches[0].Id : null;
    }

    private static string? EffectiveTitle(
        string sessionId,
        IReadOnlyDictionary<string, Session> sessions,
        IReadOnlyDictionary<string, SessionOverride> overrides)
    {
        overrides.TryGetValue(sessionId, out var sessionOverride);
        sessions.TryGetValue(sessionId, out var session);
        return sessionOverride?.TerminalTitle ?? session?.Summary;
    }

    private static string? ResolveDirectory(
        SessionOverride? sessionOverride,
        Session session,
        WorkspaceInfo? workspace)
    {
        if (sessionOverride?.LocalPath is not null &&
            Directory.Exists(sessionOverride.LocalPath))
        {
            return sessionOverride.LocalPath;
        }
        if (session.Cwd is not null && Directory.Exists(session.Cwd))
            return session.Cwd;

        var gitRoot = workspace?.GitRoot ?? session.GitRoot;
        return gitRoot is not null && Directory.Exists(gitRoot)
            ? gitRoot
            : null;
    }

    private static string? DetectDefaultShell()
    {
        var programFiles = Environment.GetFolderPath(
            Environment.SpecialFolder.ProgramFiles);
        var pwsh = Path.Combine(programFiles, "PowerShell", "7", "pwsh.exe");
        if (File.Exists(pwsh))
            return pwsh;
        return OperatingSystem.IsWindows()
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "System32",
                "WindowsPowerShell",
                "v1.0",
                "powershell.exe")
            : null;
    }

    private static WindowLayoutLaunchResult PreflightFailure(string issue) =>
        new(false, [issue], [], []);

    private static WindowLayoutWindowLaunchResult FailedWindow(
        LayoutSlotSource source,
        IReadOnlyList<TerminalLaunchFailure> failures,
        string error) =>
        new(
            source.Slot.Id,
            source.Slot.ContentKind,
            source.Slot.ContentId,
            source.ContentName ?? source.Slot.CapturedWindowTitle ?? "Layout window",
            false,
            0,
            null,
            null,
            null,
            null,
            failures,
            error);

    private static string Short(string sessionId) =>
        sessionId.Length > 8 ? sessionId[..8] : sessionId;

    private sealed record LayoutSlotSource(
        WindowLayoutSlot Slot,
        string? ContentName,
        IReadOnlyList<string> SessionIds);
}
