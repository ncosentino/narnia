using NexusLabs.Narnia.Core.Models;

namespace NexusLabs.Narnia.Web;

/// <summary>Maps captured placement onto the current monitor topology.</summary>
internal static class WindowLayoutPlacementResolver
{
    private const int MinimumWindowWidth = 160;
    private const int MinimumWindowHeight = 120;

    public static ResolvedWindowLayoutPlacement Resolve(
        WindowLayoutSlot slot,
        IReadOnlyList<WindowLayoutMonitor> monitors)
    {
        if (monitors.Count == 0)
            throw new InvalidOperationException("No desktop monitors are available.");

        var matched = monitors.FirstOrDefault(monitor =>
            string.Equals(
                monitor.DeviceName,
                slot.MonitorDeviceName,
                StringComparison.OrdinalIgnoreCase));
        var target = matched ??
            monitors.FirstOrDefault(monitor => monitor.IsPrimary) ??
            monitors[0];
        var sameWorkAreaSize =
            matched is not null &&
            target.WorkArea.Width == slot.CapturedWorkArea.Width &&
            target.WorkArea.Height == slot.CapturedWorkArea.Height;

        WindowRectangle bounds;
        WindowLayoutAdaptation adaptation;
        if (sameWorkAreaSize)
        {
            bounds = new WindowRectangle(
                target.WorkArea.X + slot.CapturedBounds.X - slot.CapturedWorkArea.X,
                target.WorkArea.Y + slot.CapturedBounds.Y - slot.CapturedWorkArea.Y,
                slot.CapturedBounds.Width,
                slot.CapturedBounds.Height);
            adaptation = WindowLayoutAdaptation.Exact;
        }
        else
        {
            bounds = new WindowRectangle(
                target.WorkArea.X +
                    (int)Math.Round(slot.NormalizedBounds.X * target.WorkArea.Width),
                target.WorkArea.Y +
                    (int)Math.Round(slot.NormalizedBounds.Y * target.WorkArea.Height),
                (int)Math.Round(slot.NormalizedBounds.Width * target.WorkArea.Width),
                (int)Math.Round(slot.NormalizedBounds.Height * target.WorkArea.Height));
            adaptation = matched is null
                ? WindowLayoutAdaptation.PrimaryMonitorFallback
                : WindowLayoutAdaptation.Scaled;
        }

        return new ResolvedWindowLayoutPlacement(
            target,
            Clamp(bounds, target.WorkArea),
            slot.WindowState,
            adaptation);
    }

    private static WindowRectangle Clamp(
        WindowRectangle bounds,
        WindowRectangle workArea)
    {
        var width = Math.Clamp(
            bounds.Width,
            Math.Min(MinimumWindowWidth, workArea.Width),
            workArea.Width);
        var height = Math.Clamp(
            bounds.Height,
            Math.Min(MinimumWindowHeight, workArea.Height),
            workArea.Height);
        var x = Math.Clamp(bounds.X, workArea.X, workArea.X + workArea.Width - width);
        var y = Math.Clamp(bounds.Y, workArea.Y, workArea.Y + workArea.Height - height);
        return new WindowRectangle(x, y, width, height);
    }
}
