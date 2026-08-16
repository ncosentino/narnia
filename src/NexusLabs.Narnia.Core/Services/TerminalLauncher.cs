using NexusLabs.Narnia.Core.Models;
using NexusLabs.Narnia.Core.Repositories;

namespace NexusLabs.Narnia.Core.Services;

/// <summary>
/// Default <see cref="ITerminalLauncher"/>. Prefers Windows Terminal (<c>wt.exe</c>) when present:
/// a single shared window is one joined <c>wt</c> command, while separate windows are one <c>wt</c>
/// invocation per tab (each creates its own window). When Windows Terminal is unavailable it falls
/// back to opening each tab in its own shell window. Process spawning is delegated to
/// <see cref="IProcessLauncher"/> so the launch logic is unit-testable.
/// </summary>
public sealed class TerminalLauncher(
    ITerminalCommandBuilder commandBuilder,
    IProcessLauncher processLauncher,
    ISessionResumeSafetyReader resumeSafetyReader,
    ISessionOperationCoordinator operationCoordinator) : ITerminalLauncher
{
    /// <inheritdoc />
    public TerminalLaunchOutcome Launch(
        string shellPath,
        string shellName,
        IReadOnlyList<TerminalLaunchTab> tabs,
        TerminalWindowMode mode,
        string copilotCommand)
    {
        if (tabs.Count == 0)
            return new TerminalLaunchOutcome([], []);

        var safeTabs = new List<TerminalLaunchTab>(tabs.Count);
        var blocked = new List<TerminalLaunchFailure>();
        var leases = new List<IDisposable>(tabs.Count);
        try
        {
            foreach (var tab in tabs)
            {
                var lease = operationCoordinator.TryAcquire(tab.SessionId);
                if (lease is null)
                {
                    blocked.Add(new TerminalLaunchFailure(
                        tab.SessionId,
                        "Session recovery or cleanup is currently in progress."));
                    continue;
                }

                var assessment = resumeSafetyReader.Inspect(tab.SessionId);
                if (assessment.Safety == SessionResumeSafety.Incompatible)
                {
                    lease.Dispose();
                    blocked.Add(new TerminalLaunchFailure(
                        tab.SessionId,
                        $"{assessment.Reason} Recover this session from its detail page."));
                }
                else
                {
                    leases.Add(lease);
                    safeTabs.Add(tab);
                }
            }

            if (safeTabs.Count == 0)
                return new TerminalLaunchOutcome([], blocked);

            var wtPath = commandBuilder.FindWindowsTerminalPath();

            var outcome = wtPath is not null &&
                mode is TerminalWindowMode.SingleWindow or TerminalWindowMode.NewWindow
                ? LaunchSingleWindow(
                    wtPath,
                    shellPath,
                    shellName,
                    safeTabs,
                    copilotCommand,
                    forceNewWindow: mode == TerminalWindowMode.NewWindow)
                : LaunchPerTab(wtPath, shellPath, shellName, safeTabs, copilotCommand);

            return blocked.Count == 0
                ? outcome
                : new TerminalLaunchOutcome(
                    outcome.LaunchedSessionIds,
                    [.. blocked, .. outcome.Failures]);
        }
        finally
        {
            foreach (var lease in leases)
                lease.Dispose();
        }
    }

    private TerminalLaunchOutcome LaunchSingleWindow(
        string wtPath,
        string shellPath,
        string shellName,
        IReadOnlyList<TerminalLaunchTab> tabs,
        string copilotCommand,
        bool forceNewWindow)
    {
        var arguments = forceNewWindow
            ? commandBuilder.BuildNewWindowCommand(
                shellPath,
                shellName,
                tabs,
                copilotCommand)
            : commandBuilder.BuildWindowCommand(
                shellPath,
                shellName,
                tabs,
                copilotCommand);
        try
        {
            processLauncher.Start(wtPath, arguments);
            return new TerminalLaunchOutcome(tabs.Select(tab => tab.SessionId).ToList(), []);
        }
        catch (Exception ex)
        {
            // The whole window is one command, so a failure fails every tab in it.
            return new TerminalLaunchOutcome(
                [],
                tabs.Select(tab => new TerminalLaunchFailure(tab.SessionId, ex.Message)).ToList());
        }
    }

    private TerminalLaunchOutcome LaunchPerTab(
        string? wtPath, string shellPath, string shellName, IReadOnlyList<TerminalLaunchTab> tabs, string copilotCommand)
    {
        var launched = new List<string>(tabs.Count);
        var failures = new List<TerminalLaunchFailure>();

        foreach (var tab in tabs)
        {
            try
            {
                if (wtPath is not null)
                    processLauncher.Start(wtPath, commandBuilder.BuildNewTabSegment(shellPath, shellName, tab, copilotCommand));
                else
                    processLauncher.Start(shellPath, commandBuilder.BuildDirectLaunchArguments(shellName, tab, copilotCommand), tab.Directory);

                launched.Add(tab.SessionId);
            }
            catch (Exception ex)
            {
                failures.Add(new TerminalLaunchFailure(tab.SessionId, ex.Message));
            }
        }

        return new TerminalLaunchOutcome(launched, failures);
    }
}
