namespace NexusLabs.Narnia.Core.Models;

/// <summary>
/// A live terminal window reconstructed from the process tree: one owning terminal
/// process hosting one or more Copilot session tabs. Only windows containing at least
/// one live Copilot agent tab are ever produced.
/// </summary>
/// <param name="TerminalProcessId">The process id of the owning terminal (e.g. <c>WindowsTerminal.exe</c>).</param>
/// <param name="Tabs">The Copilot tabs detected within this window, in tab order.</param>
public sealed record DetectedWindow(
    int TerminalProcessId,
    IReadOnlyList<DetectedTab> Tabs);

/// <summary>
/// A single Copilot session tab within a <see cref="DetectedWindow"/>.
/// </summary>
/// <param name="SessionId">The Copilot session id extracted from <c>--resume=&lt;id&gt;</c>.</param>
/// <param name="Order">Zero-based position of the tab within its window.</param>
/// <param name="Title">The terminal tab title, when recoverable from the launch command; otherwise <c>null</c>.</param>
/// <param name="Directory">The captured starting directory, when recoverable from the launch command; otherwise <c>null</c>.</param>
public sealed record DetectedTab(
    string SessionId,
    int Order,
    string? Title,
    string? Directory);
