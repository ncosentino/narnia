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
        var results = new List<WindowLayoutWindowLaunchResult>(layout.Slots.Count);
        var slotSources = new List<LayoutSlotSource>();
        foreach (var slot in layout.Slots)
        {
            if (slot.ContentKind == WindowLayoutContentKind.Collection)
            {
                if (slot.CollectionId is null ||
                    !collectionsById.TryGetValue(slot.CollectionId, out var collection))
                {
                    results.Add(FailedWindow(
                        slot,
                        slot.CapturedWindowTitle ?? $"Collection {Short(slot.ContentId)}",
                        [],
                        "The referenced Collection no longer exists."));
                    continue;
                }
                if (collection.Members.Count == 0)
                {
                    results.Add(FailedWindow(
                        slot,
                        collection.Name,
                        [],
                        "Collection has no sessions."));
                    continue;
                }

                slotSources.Add(new LayoutSlotSource(
                    slot,
                    collection.Name,
                    collection.Members.Select(member => member.SessionId).ToArray()));
            }
            else if (slot.SessionId is null)
            {
                results.Add(FailedWindow(
                    slot,
                    slot.CapturedWindowTitle ?? "Invalid session",
                    [],
                    "The referenced session is invalid."));
            }
            else
            {
                slotSources.Add(new LayoutSlotSource(slot, null, [slot.SessionId]));
            }
        }

        var failuresBySlot = slotSources.ToDictionary(
            source => source.Slot.Id,
            _ => new List<TerminalLaunchFailure>(),
            StringComparer.Ordinal);
        var candidateSessionIdsBySlot = slotSources.ToDictionary(
            source => source.Slot.Id,
            _ => new List<string>(),
            StringComparer.Ordinal);
        var ownerBySessionId =
            new Dictionary<string, LayoutSlotSource>(StringComparer.OrdinalIgnoreCase);

        // An explicit individual-session window wins when the same session also belongs to a
        // Collection window. The Collection can still launch its remaining members.
        foreach (var source in slotSources
            .OrderBy(source =>
                source.Slot.ContentKind == WindowLayoutContentKind.Session ? 0 : 1)
            .ThenBy(source => source.Slot.SlotOrder))
        {
            foreach (var sessionId in source.SessionIds)
            {
                if (ownerBySessionId.TryGetValue(sessionId, out var owner))
                {
                    failuresBySlot[source.Slot.Id].Add(new TerminalLaunchFailure(
                        sessionId,
                        $"This session is assigned to another Layout window ({DisplayName(owner)})."));
                    continue;
                }

                ownerBySessionId[sessionId] = source;
                candidateSessionIdsBySlot[source.Slot.Id].Add(sessionId);
            }
        }

        var allSessionIds = candidateSessionIdsBySlot.Values
            .SelectMany(sessionIds => sessionIds)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var activeSessionIds = activityReader.GetActiveSessionIds();
        var sessions = await sessionsRepository.GetByIdsAsync(allSessionIds, ct);
        foreach (var source in slotSources)
        {
            var candidates = candidateSessionIdsBySlot[source.Slot.Id];
            for (var index = candidates.Count - 1; index >= 0; index--)
            {
                var sessionId = candidates[index];
                string? reason = null;
                if (activeSessionIds.Contains(sessionId))
                    reason = "Session is already active.";
                else if (!sessions.ContainsKey(sessionId))
                    reason = "Session is unavailable.";

                if (reason is null)
                    continue;

                candidates.RemoveAt(index);
                failuresBySlot[source.Slot.Id].Add(
                    new TerminalLaunchFailure(sessionId, reason));
            }
        }

        var overrides = await overridesRepository.GetAllOverridesAsync(ct);
        var resolvedSlots = slotSources
            .Select(source => source with
            {
                ContentName = source.ContentName ??
                    (source.SessionIds.FirstOrDefault() is { } sessionId &&
                     sessions.TryGetValue(sessionId, out var session)
                        ? session.Summary
                        : null) ??
                    source.Slot.CapturedWindowTitle ??
                    $"Session {Short(source.Slot.ContentId)}",
            })
            .ToArray();
        var tabsBySlot = resolvedSlots.ToDictionary(
            source => source.Slot.Id,
            source => candidateSessionIdsBySlot[source.Slot.Id]
                .Select(sessionId => BuildTab(
                    sessionId,
                    sessions[sessionId],
                    overrides.TryGetValue(sessionId, out var sessionOverride)
                        ? sessionOverride
                        : null))
                .ToArray(),
            StringComparer.Ordinal);
        var allTabs = tabsBySlot.Values.SelectMany(tabs => tabs).ToArray();
        IReadOnlyList<LaunchDirectoryCollision> collisions = [];
        if (!force && allTabs.Length > 0)
        {
            collisions = await collisionDetector.DetectAsync(allTabs, ct);
            foreach (var collisionGroup in collisions.GroupBy(
                collision => collision.SessionId,
                StringComparer.OrdinalIgnoreCase))
            {
                var source = resolvedSlots.FirstOrDefault(candidate =>
                    tabsBySlot[candidate.Slot.Id].Any(tab =>
                        string.Equals(
                            tab.SessionId,
                            collisionGroup.Key,
                            StringComparison.OrdinalIgnoreCase)));
                if (source is null)
                    continue;

                tabsBySlot[source.Slot.Id] = tabsBySlot[source.Slot.Id]
                    .Where(tab => !string.Equals(
                        tab.SessionId,
                        collisionGroup.Key,
                        StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                foreach (var collision in collisionGroup)
                {
                    failuresBySlot[source.Slot.Id].Add(new TerminalLaunchFailure(
                        collision.SessionId,
                        collision.Describe()));
                }
            }
        }

        if (tabsBySlot.Values.All(tabs => tabs.Length == 0))
        {
            foreach (var source in resolvedSlots)
            {
                results.Add(FailedWindow(
                    source.Slot,
                    source.ContentName!,
                    failuresBySlot[source.Slot.Id],
                    "No eligible sessions remain for this Layout window."));
            }

            return Complete(results, collisions);
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

        foreach (var source in resolvedSlots
            .OrderByDescending(item => item.Slot.ZOrder)
            .ThenByDescending(item => item.Slot.SlotOrder))
        {
            var tabs = tabsBySlot[source.Slot.Id];
            var preflightFailures = failuresBySlot[source.Slot.Id];
            if (tabs.Length == 0)
            {
                results.Add(FailedWindow(
                    source.Slot,
                    source.ContentName!,
                    preflightFailures,
                    "No eligible sessions remain for this Layout window."));
                continue;
            }

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
                    source.Slot,
                    source.ContentName!,
                    [.. preflightFailures, .. outcome.Failures],
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
                    source.Slot,
                    source.ContentName!,
                    [.. preflightFailures, .. outcome.Failures],
                    "Windows Terminal did not expose a new window before the timeout."));
                continue;
            }

            var placement = WindowLayoutPlacementResolver.Resolve(
                source.Slot,
                capture.Monitors);
            var applied = platform.ApplyPlacement(window.Handle, placement);
            var combinedFailures =
                new List<TerminalLaunchFailure>(preflightFailures.Count + outcome.Failures.Count);
            combinedFailures.AddRange(preflightFailures);
            combinedFailures.AddRange(outcome.Failures);
            results.Add(new WindowLayoutWindowLaunchResult(
                source.Slot.Id,
                source.Slot.ContentKind,
                source.Slot.ContentId,
                source.ContentName!,
                applied.Success && combinedFailures.Count == 0,
                outcome.LaunchedSessionIds.Count,
                window.Handle,
                placement.Adaptation,
                placement.Bounds,
                applied.ActualBounds,
                combinedFailures,
                applied.Error));
        }

        return Complete(results, collisions);

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
        WindowLayoutSlot slot,
        string contentName,
        IReadOnlyList<TerminalLaunchFailure> failures,
        string error) =>
        new(
            slot.Id,
            slot.ContentKind,
            slot.ContentId,
            contentName,
            false,
            0,
            null,
            null,
            null,
            null,
            failures,
            error);

    private static WindowLayoutLaunchResult Complete(
        IReadOnlyList<WindowLayoutWindowLaunchResult> results,
        IReadOnlyList<LaunchDirectoryCollision> collisions) =>
        new(
            true,
            [],
            collisions,
            [.. results.OrderBy(result => result.SlotId, StringComparer.Ordinal)]);

    private static string DisplayName(LayoutSlotSource source) =>
        source.ContentName ??
        source.Slot.CapturedWindowTitle ??
        (source.Slot.ContentKind == WindowLayoutContentKind.Collection
            ? $"Collection {Short(source.Slot.ContentId)}"
            : $"Session {Short(source.Slot.ContentId)}");

    private static string Short(string sessionId) =>
        sessionId.Length > 8 ? sessionId[..8] : sessionId;

    private sealed record LayoutSlotSource(
        WindowLayoutSlot Slot,
        string? ContentName,
        IReadOnlyList<string> SessionIds);
}
