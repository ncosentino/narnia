using NexusLabs.Narnia.Core.Models;
using NexusLabs.Narnia.Core.Services;

namespace NexusLabs.Narnia.Web;

/// <summary>Captures desktop placement and launches persisted Collection layouts.</summary>
public interface IWindowLayoutService
{
    /// <summary>Captures current terminal windows with suggested Collection matches.</summary>
    ValueTask<WindowLayoutCaptureView> CaptureAsync(CancellationToken ct);

    /// <summary>Launches and positions every Collection window in a persisted Layout.</summary>
    ValueTask<WindowLayoutLaunchResult> LaunchAsync(
        WindowLayout layout,
        bool force,
        CancellationToken ct);
}

/// <summary>Capture data prepared for the Layout creation UI.</summary>
public sealed record WindowLayoutCaptureView(
    bool IsAvailable,
    string? UnavailableReason,
    IReadOnlyList<WindowLayoutCaptureCandidate> Windows);

/// <summary>One captured terminal window and its suggested Collection.</summary>
public sealed record WindowLayoutCaptureCandidate(
    CapturedTerminalWindow Window,
    string? SuggestedCollectionId);

/// <summary>Outcome of a persisted Layout launch.</summary>
public sealed record WindowLayoutLaunchResult(
    bool PreflightPassed,
    IReadOnlyList<string> Issues,
    IReadOnlyList<LaunchDirectoryCollision> Collisions,
    IReadOnlyList<WindowLayoutWindowLaunchResult> Windows)
{
    /// <summary>Gets whether every Layout window launched and positioned successfully.</summary>
    public bool Success =>
        PreflightPassed &&
        Issues.Count == 0 &&
        Windows.Count > 0 &&
        Windows.All(window => window.Success);
}

/// <summary>Launch and placement outcome for one Layout slot.</summary>
public sealed record WindowLayoutWindowLaunchResult(
    string SlotId,
    string CollectionId,
    string CollectionName,
    bool Success,
    int LaunchedSessions,
    long? WindowHandle,
    WindowLayoutAdaptation? Adaptation,
    WindowRectangle? RequestedBounds,
    WindowRectangle? ActualBounds,
    IReadOnlyList<TerminalLaunchFailure> Failures,
    string? Error);
