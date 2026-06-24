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
    string BuildShellArguments(string shellName, string sessionId);

    /// <summary>
    /// Builds a single <c>new-tab</c> segment for the given tab, including its title and
    /// (when present) starting directory.
    /// </summary>
    string BuildNewTabSegment(string shellPath, string shellName, TerminalLaunchTab tab);

    /// <summary>
    /// Builds the full <c>wt.exe</c> argument string that opens one window containing all the
    /// given tabs in order.
    /// </summary>
    string BuildWindowCommand(string shellPath, string shellName, IReadOnlyList<TerminalLaunchTab> tabs);

    /// <summary>
    /// Builds the shell arguments for launching a tab directly (without Windows Terminal), including
    /// a best-effort window-title set. Used as the fallback when <c>wt.exe</c> is unavailable.
    /// </summary>
    string BuildDirectLaunchArguments(string shellName, TerminalLaunchTab tab);
}
