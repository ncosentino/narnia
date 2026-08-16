using NexusLabs.Narnia.Core.Models;

namespace NexusLabs.Narnia.Web.Tests;

public sealed class WindowLayoutPlacementResolverTests
{
    [Fact]
    public void Resolve_SameMonitorAndResolution_TranslatesExactOffsets()
    {
        var slot = Slot(
            @"\\.\DISPLAY2",
            new WindowRectangle(3840, 0, 2560, 1400),
            new WindowRectangle(3840, 0, 1280, 700),
            new NormalizedWindowRectangle(0, 0, 0.5, 0.5));
        var monitor = Monitor(
            @"\\.\DISPLAY2",
            false,
            new WindowRectangle(-2560, 0, 2560, 1440),
            new WindowRectangle(-2560, 0, 2560, 1400));

        var resolved = WindowLayoutPlacementResolver.Resolve(slot, [monitor]);

        Assert.Equal(WindowLayoutAdaptation.Exact, resolved.Adaptation);
        Assert.Equal(new WindowRectangle(-2560, 0, 1280, 700), resolved.Bounds);
    }

    [Fact]
    public void Resolve_ChangedResolution_ScalesNormalizedBounds()
    {
        var slot = Slot(
            @"\\.\DISPLAY1",
            new WindowRectangle(0, 0, 3840, 2112),
            new WindowRectangle(2560, 1056, 1280, 1056),
            new NormalizedWindowRectangle(2d / 3d, 0.5, 1d / 3d, 0.5));
        var monitor = Monitor(
            @"\\.\DISPLAY1",
            true,
            new WindowRectangle(0, 0, 2560, 1440),
            new WindowRectangle(0, 0, 2560, 1400));

        var resolved = WindowLayoutPlacementResolver.Resolve(slot, [monitor]);

        Assert.Equal(WindowLayoutAdaptation.Scaled, resolved.Adaptation);
        Assert.Equal(new WindowRectangle(1707, 700, 853, 700), resolved.Bounds);
    }

    [Fact]
    public void Resolve_MissingMonitor_UsesPrimaryAndKeepsWindowOnScreen()
    {
        var slot = Slot(
            @"\\.\MISSING",
            new WindowRectangle(0, 0, 3840, 2112),
            new WindowRectangle(3200, 1800, 1200, 800),
            new NormalizedWindowRectangle(0.833, 0.852, 0.313, 0.379));
        var primary = Monitor(
            @"\\.\DISPLAY1",
            true,
            new WindowRectangle(0, 0, 1920, 1080),
            new WindowRectangle(0, 0, 1920, 1040));

        var resolved = WindowLayoutPlacementResolver.Resolve(slot, [primary]);

        Assert.Equal(WindowLayoutAdaptation.PrimaryMonitorFallback, resolved.Adaptation);
        Assert.True(resolved.Bounds.X >= primary.WorkArea.X);
        Assert.True(resolved.Bounds.Y >= primary.WorkArea.Y);
        Assert.True(resolved.Bounds.X + resolved.Bounds.Width <=
            primary.WorkArea.X + primary.WorkArea.Width);
        Assert.True(resolved.Bounds.Y + resolved.Bounds.Height <=
            primary.WorkArea.Y + primary.WorkArea.Height);
    }

    private static WindowLayoutSlot Slot(
        string monitor,
        WindowRectangle workArea,
        WindowRectangle bounds,
        NormalizedWindowRectangle normalized) =>
        new(
            "slot",
            "layout",
            0,
            "collection",
            null,
            monitor,
            true,
            workArea,
            bounds,
            normalized,
            WindowLayoutState.Normal,
            0,
            WindowLayoutDesktopPolicy.Current);

    private static WindowLayoutMonitor Monitor(
        string name,
        bool primary,
        WindowRectangle bounds,
        WindowRectangle workArea) =>
        new(name, primary, bounds, workArea);
}
