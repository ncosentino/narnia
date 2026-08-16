namespace NexusLabs.Narnia.Core.Services;

/// <summary>
/// How a set of sessions should be arranged across terminal windows when launched.
/// </summary>
public enum TerminalWindowMode
{
    /// <summary>All sessions open as tabs within a single terminal window.</summary>
    SingleWindow,

    /// <summary>All sessions open as tabs within one newly created terminal window.</summary>
    NewWindow,

    /// <summary>Each session opens in its own separate terminal window.</summary>
    SeparateWindows,
}
