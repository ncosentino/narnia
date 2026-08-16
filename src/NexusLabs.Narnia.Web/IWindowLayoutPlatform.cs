namespace NexusLabs.Narnia.Web;

/// <summary>Reads and positions top-level terminal windows through the host desktop APIs.</summary>
public interface IWindowLayoutPlatform
{
    /// <summary>Gets whether desktop layout capture is supported on this platform.</summary>
    bool IsSupported { get; }

    /// <summary>Captures visible top-level Windows Terminal HWNDs and monitor work areas.</summary>
    WindowLayoutCaptureSnapshot Capture();

    /// <summary>Waits for a new terminal HWND not present in the supplied baseline.</summary>
    ValueTask<CapturedTerminalWindow?> WaitForNewTerminalWindowAsync(
        IReadOnlySet<long> existingHandles,
        IReadOnlyCollection<string> expectedTitles,
        TimeSpan timeout,
        CancellationToken ct);

    /// <summary>Applies resolved bounds and show state to a live HWND.</summary>
    WindowLayoutPlacementResult ApplyPlacement(
        long handle,
        ResolvedWindowLayoutPlacement placement);
}
