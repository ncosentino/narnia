using NexusLabs.Narnia.Core.Models;

namespace NexusLabs.Narnia.Web;

/// <summary>A read-only desktop snapshot used to create or restore a persisted Layout.</summary>
public sealed record WindowLayoutCaptureSnapshot(
    bool IsAvailable,
    string? UnavailableReason,
    IReadOnlyList<CapturedTerminalWindow> Windows,
    IReadOnlyList<WindowLayoutMonitor> Monitors);

/// <summary>A visible top-level Windows Terminal window observed by HWND.</summary>
public sealed record CapturedTerminalWindow(
    long Handle,
    int ProcessId,
    string Title,
    int ZOrder,
    WindowRectangle Bounds,
    WindowLayoutState State,
    WindowLayoutMonitor Monitor);

/// <summary>A monitor and its usable work area.</summary>
public sealed record WindowLayoutMonitor(
    string DeviceName,
    bool IsPrimary,
    WindowRectangle Bounds,
    WindowRectangle WorkArea);

/// <summary>A saved slot mapped onto the current monitor topology.</summary>
public sealed record ResolvedWindowLayoutPlacement(
    WindowLayoutMonitor Monitor,
    WindowRectangle Bounds,
    WindowLayoutState State,
    WindowLayoutAdaptation Adaptation);

/// <summary>How restore adapted captured placement to the current desktop.</summary>
public enum WindowLayoutAdaptation
{
    /// <summary>The captured monitor and work-area dimensions still match.</summary>
    Exact,

    /// <summary>The captured monitor exists but its work-area dimensions changed.</summary>
    Scaled,

    /// <summary>The captured monitor was unavailable and the primary monitor was used.</summary>
    PrimaryMonitorFallback,
}

/// <summary>Result of applying placement to one live window.</summary>
public sealed record WindowLayoutPlacementResult(
    bool Success,
    WindowRectangle? ActualBounds,
    string? Error);
