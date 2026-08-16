namespace NexusLabs.Narnia.Core.Services;

/// <summary>
/// A single tab to launch in a terminal window.
/// </summary>
/// <param name="SessionId">The Copilot session id to resume.</param>
/// <param name="Title">The terminal tab title.</param>
/// <param name="Directory">The starting directory, or <c>null</c> to inherit the default.</param>
public sealed record TerminalLaunchTab(string SessionId, string Title, string? Directory);

/// <summary>
/// Builds Windows Terminal command lines for launching Copilot sessions, so that the bulk
/// launch and the window reopen paths produce identical commands. Locating the terminal and
/// composing the arguments are separated from actually starting the process, keeping this
/// unit-testable.
/// </summary>
public interface ITerminalCommandBuilder
{
    /// <summary>
    /// Returns the path to <c>wt.exe</c> if Windows Terminal is installed for the current user,
    /// or <c>null</c> when it is not available.
    /// </summary>
    string? FindWindowsTerminalPath();

    /// <summary>
    /// Builds the shell arguments that resume the given session (e.g. the
    /// <c>-NoExit -Command "copilot --resume=&lt;id&gt;"</c> portion for PowerShell).
    /// </summary>
    /// <param name="copilotCommand">
    /// The command that invokes Copilot (e.g. <c>copilot</c>, or <c>agency copilot</c> when a
    /// wrapper is required). Embedded verbatim as source text for a freshly-launched shell, so a
    /// multi-word value is parsed correctly with no special handling needed here.
    /// </param>
    string BuildShellArguments(string shellName, string sessionId, string copilotCommand);

    /// <summary>
    /// Builds a single <c>new-tab</c> segment for the given tab, including its title and
    /// (when present) starting directory.
    /// </summary>
    /// <param name="copilotCommand">See <see cref="BuildShellArguments"/>.</param>
    string BuildNewTabSegment(string shellPath, string shellName, TerminalLaunchTab tab, string copilotCommand);

    /// <summary>
    /// Builds the full <c>wt.exe</c> argument string that opens one window containing all the
    /// given tabs in order.
    /// </summary>
    /// <param name="copilotCommand">See <see cref="BuildShellArguments"/>.</param>
    string BuildWindowCommand(
        string shellPath, string shellName, IReadOnlyList<TerminalLaunchTab> tabs, string copilotCommand);

    /// <summary>
    /// Builds a <c>wt.exe</c> argument string that forces one new window containing all tabs.
    /// </summary>
    /// <param name="copilotCommand">See <see cref="BuildShellArguments"/>.</param>
    string BuildNewWindowCommand(
        string shellPath,
        string shellName,
        IReadOnlyList<TerminalLaunchTab> tabs,
        string copilotCommand);

    /// <summary>
    /// Builds the shell arguments for launching a tab directly (without Windows Terminal), including
    /// a best-effort window-title set. Used as the fallback when <c>wt.exe</c> is unavailable.
    /// </summary>
    /// <param name="copilotCommand">See <see cref="BuildShellArguments"/>.</param>
    string BuildDirectLaunchArguments(string shellName, TerminalLaunchTab tab, string copilotCommand);
}
