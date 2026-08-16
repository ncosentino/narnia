namespace NexusLabs.Narnia.Core.Models;

/// <summary>A persisted composition of Collections and their desired terminal-window placement.</summary>
/// <param name="Id">Narnia-assigned stable identifier.</param>
/// <param name="Name">Case-insensitively unique display name.</param>
/// <param name="CreatedAt">When the layout was created.</param>
/// <param name="UpdatedAt">When the layout name or slots last changed.</param>
/// <param name="Monitors">Persisted monitor topology used by the editor and restore.</param>
/// <param name="Slots">Collection-backed terminal windows in the layout.</param>
public sealed record WindowLayout(
    string Id,
    string Name,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<WindowLayoutMonitorDefinition> Monitors,
    IReadOnlyList<WindowLayoutSlot> Slots);

/// <summary>A terminal window and its captured desktop placement.</summary>
/// <param name="Id">Narnia-assigned stable slot identifier.</param>
/// <param name="LayoutId">Owning layout identifier.</param>
/// <param name="SlotOrder">Stable display and launch order.</param>
/// <param name="ContentKind">Whether this window contains a Collection or one session.</param>
/// <param name="CollectionId">Collection whose current members open in this window.</param>
/// <param name="SessionId">Individual session that opens in this window.</param>
/// <param name="CapturedWindowTitle">Window title observed during capture.</param>
/// <param name="MonitorDeviceName">Captured Win32 monitor device name.</param>
/// <param name="MonitorIsPrimary">Whether the captured monitor was primary.</param>
/// <param name="CapturedWorkArea">Captured monitor work area.</param>
/// <param name="CapturedBounds">Captured extended-frame window bounds.</param>
/// <param name="NormalizedBounds">Bounds normalized to the captured work area.</param>
/// <param name="WindowState">Captured normal, maximized, or minimized state.</param>
/// <param name="ZOrder">Zero-based top-to-bottom window order during capture.</param>
/// <param name="DesktopPolicy">Virtual-desktop restore policy.</param>
public sealed record WindowLayoutSlot(
    string Id,
    string LayoutId,
    int SlotOrder,
    WindowLayoutContentKind ContentKind,
    string? CollectionId,
    string? SessionId,
    string? CapturedWindowTitle,
    string MonitorDeviceName,
    bool MonitorIsPrimary,
    WindowRectangle CapturedWorkArea,
    WindowRectangle CapturedBounds,
    NormalizedWindowRectangle NormalizedBounds,
    WindowLayoutState WindowState,
    int ZOrder,
    WindowLayoutDesktopPolicy DesktopPolicy)
{
    /// <summary>Gets the Collection or session identifier represented by this slot.</summary>
    public string ContentId => CollectionId ?? SessionId ?? "";
}

/// <summary>Placement values used when creating or replacing a layout slot.</summary>
public sealed record WindowLayoutSlotDefinition(
    int SlotOrder,
    WindowLayoutContentKind ContentKind,
    string? CollectionId,
    string? SessionId,
    string? CapturedWindowTitle,
    string MonitorDeviceName,
    bool MonitorIsPrimary,
    WindowRectangle CapturedWorkArea,
    WindowRectangle CapturedBounds,
    NormalizedWindowRectangle NormalizedBounds,
    WindowLayoutState WindowState,
    int ZOrder,
    WindowLayoutDesktopPolicy DesktopPolicy);

/// <summary>A persisted monitor available to the Layout editor.</summary>
public sealed record WindowLayoutMonitorDefinition(
    int MonitorOrder,
    string DeviceName,
    bool IsPrimary,
    WindowRectangle CapturedBounds,
    WindowRectangle CapturedWorkArea);

/// <summary>An integer desktop rectangle.</summary>
public sealed record WindowRectangle(int X, int Y, int Width, int Height);

/// <summary>A rectangle normalized to a monitor work area.</summary>
public sealed record NormalizedWindowRectangle(double X, double Y, double Width, double Height);

/// <summary>Persisted top-level window state.</summary>
public enum WindowLayoutState
{
    /// <summary>The window uses restored bounds.</summary>
    Normal,

    /// <summary>The window is maximized after placement.</summary>
    Maximized,

    /// <summary>The window is minimized after placement.</summary>
    Minimized,
}

/// <summary>Controls which virtual desktop receives a restored layout window.</summary>
public enum WindowLayoutDesktopPolicy
{
    /// <summary>Restore onto the virtual desktop that is current at launch time.</summary>
    Current,
}

/// <summary>Content represented by one Layout window.</summary>
public enum WindowLayoutContentKind
{
    /// <summary>The window launches the current members of one Collection.</summary>
    Collection,

    /// <summary>The window launches one exact Copilot session.</summary>
    Session,
}
