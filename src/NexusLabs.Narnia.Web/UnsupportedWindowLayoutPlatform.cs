namespace NexusLabs.Narnia.Web;

/// <summary>Reports that desktop Layouts require Windows.</summary>
public sealed class UnsupportedWindowLayoutPlatform : IWindowLayoutPlatform
{
    /// <inheritdoc />
    public bool IsSupported => false;

    /// <inheritdoc />
    public WindowLayoutCaptureSnapshot Capture() =>
        new(
            false,
            "Window Layout capture currently requires Windows and Windows Terminal.",
            [],
            []);

    /// <inheritdoc />
    public ValueTask<CapturedTerminalWindow?> WaitForNewTerminalWindowAsync(
        IReadOnlySet<long> existingHandles,
        IReadOnlyCollection<string> expectedTitles,
        TimeSpan timeout,
        CancellationToken ct) =>
        ValueTask.FromResult<CapturedTerminalWindow?>(null);

    /// <inheritdoc />
    public WindowLayoutPlacementResult ApplyPlacement(
        long handle,
        ResolvedWindowLayoutPlacement placement) =>
        new(false, null, "Window Layout restore is unavailable on this platform.");
}
