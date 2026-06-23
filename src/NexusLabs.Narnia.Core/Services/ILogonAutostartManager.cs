namespace NexusLabs.Narnia.Core.Services;

/// <summary>
/// Installs or removes a per-user logon autostart entry for the Narnia server, so the
/// background snapshotter is running from login onward. Opt-in and disabled by default.
/// </summary>
public interface ILogonAutostartManager
{
    /// <summary>Whether logon autostart can be managed on the current platform.</summary>
    bool IsSupported { get; }

    /// <summary>Returns whether the autostart entry is currently installed.</summary>
    bool IsEnabled();

    /// <summary>Installs the autostart entry (idempotent).</summary>
    void Enable();

    /// <summary>Removes the autostart entry (idempotent).</summary>
    void Disable();
}
