namespace NexusLabs.Narnia.Core.Services;

/// <summary>
/// The single entry point for opening Copilot sessions in a terminal. Every caller — the single
/// session page, the bulk launch on the sessions list, and the window/group reopen paths — resolves
/// its tabs and then delegates here, so terminal selection (Windows Terminal vs a direct shell),
/// window grouping, and process spawning all behave identically and live in one place.
/// </summary>
public interface ITerminalLauncher
{
    /// <summary>
    /// Launches the given tabs using the provided shell, arranged according to
    /// <paramref name="mode"/>. When Windows Terminal is available it is preferred; otherwise each
    /// tab is opened in its own shell window. Failures are captured per session rather than thrown.
    /// </summary>
    /// <param name="shellPath">Full path to the shell executable (e.g. pwsh.exe).</param>
    /// <param name="shellName">Lowercased shell name (e.g. <c>pwsh</c>, <c>cmd</c>).</param>
    /// <param name="tabs">The tabs to launch, in order.</param>
    /// <param name="mode">Whether to open one shared window or one window per session.</param>
    /// <returns>Which sessions launched and which failed.</returns>
    TerminalLaunchOutcome Launch(
        string shellPath,
        string shellName,
        IReadOnlyList<TerminalLaunchTab> tabs,
        TerminalWindowMode mode);
}

/// <summary>The result of a launch: the sessions that started and those that failed.</summary>
/// <param name="LaunchedSessionIds">Session ids that were launched.</param>
/// <param name="Failures">Per-session failures, if any.</param>
public sealed record TerminalLaunchOutcome(
    IReadOnlyList<string> LaunchedSessionIds,
    IReadOnlyList<TerminalLaunchFailure> Failures);

/// <summary>A single session that failed to launch and why.</summary>
/// <param name="SessionId">The session that failed.</param>
/// <param name="Reason">A human-readable failure reason.</param>
public sealed record TerminalLaunchFailure(string SessionId, string Reason);
