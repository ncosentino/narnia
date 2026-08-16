using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using NexusLabs.Narnia.Core.Models;

namespace NexusLabs.Narnia.Web;

/// <summary>Captures and positions top-level Windows Terminal windows by HWND.</summary>
public sealed class WindowsWindowLayoutPlatform : IWindowLayoutPlatform
{
    private const int DwmExtendedFrameBounds = 9;
    private const uint MonitorDefaultToNearest = 2;
    private const int SwHide = 0;
    private const int SwShowNormal = 1;
    private const int SwShowMinimized = 2;
    private const int SwShowMaximized = 3;
    private const int SwRestore = 9;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoZOrder = 0x0004;
    private const int PlacementTolerance = 12;

    /// <inheritdoc />
    public bool IsSupported => true;

    /// <inheritdoc />
    public WindowLayoutCaptureSnapshot Capture()
    {
        var terminalProcesses = Process.GetProcessesByName("WindowsTerminal");
        var terminalProcessIds = terminalProcesses
            .Select(process => process.Id)
            .ToHashSet();
        foreach (var process in terminalProcesses)
            process.Dispose();
        var monitors = EnumerateMonitors();
        var windows = new List<CapturedTerminalWindow>();
        var zOrder = 0;

        NativeMethods.EnumWindows((handle, _) =>
        {
            if (!NativeMethods.IsWindowVisible(handle))
                return true;

            NativeMethods.GetWindowThreadProcessId(handle, out var processId);
            if (!terminalProcessIds.Contains((int)processId))
                return true;

            var monitorHandle = NativeMethods.MonitorFromWindow(
                handle,
                MonitorDefaultToNearest);
            var monitor = monitors.FirstOrDefault(candidate =>
                candidate.Handle == monitorHandle);
            if (monitor is null)
                return true;

            var placement = WindowPlacement.Create();
            if (!NativeMethods.GetWindowPlacement(handle, ref placement))
                return true;

            var state = NativeMethods.IsIconic(handle)
                ? WindowLayoutState.Minimized
                : NativeMethods.IsZoomed(handle)
                    ? WindowLayoutState.Maximized
                    : WindowLayoutState.Normal;
            var bounds = state == WindowLayoutState.Minimized
                ? WorkspaceToScreen(
                    placement.NormalPosition,
                    monitor.Model)
                : GetExtendedFrameBounds(handle);
            if (bounds.Width <= 0 || bounds.Height <= 0)
                return true;

            windows.Add(new CapturedTerminalWindow(
                handle.ToInt64(),
                (int)processId,
                GetWindowTitle(handle),
                zOrder++,
                bounds,
                state,
                monitor.Model));
            return true;
        }, IntPtr.Zero);

        return new WindowLayoutCaptureSnapshot(
            true,
            null,
            windows,
            monitors.Select(monitor => monitor.Model).ToArray());
    }

    /// <inheritdoc />
    public async ValueTask<CapturedTerminalWindow?> WaitForNewTerminalWindowAsync(
        IReadOnlySet<long> existingHandles,
        IReadOnlyCollection<string> expectedTitles,
        TimeSpan timeout,
        CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            var candidates = Capture().Windows
                .Where(window => !existingHandles.Contains(window.Handle))
                .ToArray();
            var matching = candidates
                .Where(window => expectedTitles.Contains(
                    window.Title,
                    StringComparer.OrdinalIgnoreCase))
                .ToArray();
            if (matching.Length == 1)
                return matching[0];

            await Task.Delay(200, ct);
        }

        return null;
    }

    /// <inheritdoc />
    public WindowLayoutPlacementResult ApplyPlacement(
        long handle,
        ResolvedWindowLayoutPlacement placement)
    {
        var window = new IntPtr(handle);
        if (!NativeMethods.IsWindow(window))
            return new(false, null, "The launched terminal window no longer exists.");

        NativeMethods.ShowWindow(window, SwRestore);
        var bounds = placement.Bounds;
        var currentOuterBounds = GetOuterBounds(window);
        var currentFrameBounds = GetExtendedFrameBounds(window);
        var leftFrame = currentFrameBounds.X - currentOuterBounds.X;
        var topFrame = currentFrameBounds.Y - currentOuterBounds.Y;
        var rightFrame =
            currentOuterBounds.X + currentOuterBounds.Width -
            currentFrameBounds.X -
            currentFrameBounds.Width;
        var bottomFrame =
            currentOuterBounds.Y + currentOuterBounds.Height -
            currentFrameBounds.Y -
            currentFrameBounds.Height;
        if (!NativeMethods.SetWindowPos(
                window,
                IntPtr.Zero,
                bounds.X - leftFrame,
                bounds.Y - topFrame,
                bounds.Width + leftFrame + rightFrame,
                bounds.Height + topFrame + bottomFrame,
                SwpNoActivate | SwpNoZOrder))
        {
            return new(false, null, $"SetWindowPos failed with Win32 error {Marshal.GetLastWin32Error()}.");
        }

        switch (placement.State)
        {
            case WindowLayoutState.Maximized:
                NativeMethods.ShowWindow(window, SwShowMaximized);
                break;
            case WindowLayoutState.Minimized:
                NativeMethods.ShowWindow(window, SwShowMinimized);
                break;
        }

        var actual = placement.State == WindowLayoutState.Normal
            ? GetExtendedFrameBounds(window)
            : bounds;
        if (placement.State == WindowLayoutState.Normal &&
            !WithinTolerance(bounds, actual))
        {
            return new(
                false,
                actual,
                $"The terminal window did not reach the requested bounds within {PlacementTolerance} pixels.");
        }

        return new(true, actual, null);
    }

    private static IReadOnlyList<MonitorHandle> EnumerateMonitors()
    {
        var monitors = new List<MonitorHandle>();
        NativeMethods.EnumDisplayMonitors(
            IntPtr.Zero,
            IntPtr.Zero,
            (monitor, _, _, _) =>
            {
                var info = MonitorInfo.Create();
                if (NativeMethods.GetMonitorInfo(monitor, ref info))
                {
                    monitors.Add(new MonitorHandle(
                        monitor,
                        new WindowLayoutMonitor(
                            info.DeviceName,
                            (info.Flags & 1) == 1,
                            ToRectangle(info.Monitor),
                            ToRectangle(info.WorkArea))));
                }

                return true;
            },
            IntPtr.Zero);
        return monitors;
    }

    private static WindowRectangle GetExtendedFrameBounds(IntPtr handle)
    {
        if (NativeMethods.DwmGetWindowAttribute(
                handle,
                DwmExtendedFrameBounds,
                out var rect,
                Marshal.SizeOf<NativeRect>()) != 0)
        {
            NativeMethods.GetWindowRect(handle, out rect);
        }

        return ToRectangle(rect);
    }

    private static WindowRectangle GetOuterBounds(IntPtr handle)
    {
        NativeMethods.GetWindowRect(handle, out var rect);
        return ToRectangle(rect);
    }

    private static WindowRectangle WorkspaceToScreen(
        NativeRect rectangle,
        WindowLayoutMonitor monitor)
    {
        var xOffset = monitor.WorkArea.X - monitor.Bounds.X;
        var yOffset = monitor.WorkArea.Y - monitor.Bounds.Y;
        return new WindowRectangle(
            rectangle.Left + xOffset,
            rectangle.Top + yOffset,
            rectangle.Right - rectangle.Left,
            rectangle.Bottom - rectangle.Top);
    }

    private static bool WithinTolerance(
        WindowRectangle expected,
        WindowRectangle actual) =>
        Math.Abs(expected.X - actual.X) <= PlacementTolerance &&
        Math.Abs(expected.Y - actual.Y) <= PlacementTolerance &&
        Math.Abs(expected.Width - actual.Width) <= PlacementTolerance &&
        Math.Abs(expected.Height - actual.Height) <= PlacementTolerance;

    private static string GetWindowTitle(IntPtr handle)
    {
        var length = NativeMethods.GetWindowTextLength(handle);
        var buffer = new StringBuilder(Math.Max(length + 1, 256));
        NativeMethods.GetWindowText(handle, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    private static WindowRectangle ToRectangle(NativeRect rect) =>
        new(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);

    private sealed record MonitorHandle(IntPtr Handle, WindowLayoutMonitor Model);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPlacement
    {
        public int Length;
        public int Flags;
        public int ShowCommand;
        public NativePoint MinimumPosition;
        public NativePoint MaximumPosition;
        public NativeRect NormalPosition;

        public static WindowPlacement Create() =>
            new() { Length = Marshal.SizeOf<WindowPlacement>() };
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect WorkArea;
        public int Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;

        public static MonitorInfo Create() =>
            new()
            {
                Size = Marshal.SizeOf<MonitorInfo>(),
                DeviceName = string.Empty,
            };
    }

    private static class NativeMethods
    {
        internal delegate bool EnumWindowsCallback(IntPtr handle, IntPtr parameter);
        internal delegate bool MonitorEnumCallback(
            IntPtr monitor,
            IntPtr deviceContext,
            IntPtr rect,
            IntPtr parameter);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool EnumWindows(
            EnumWindowsCallback callback,
            IntPtr parameter);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool EnumDisplayMonitors(
            IntPtr deviceContext,
            IntPtr clipRect,
            MonitorEnumCallback callback,
            IntPtr parameter);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsWindowVisible(IntPtr handle);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsWindow(IntPtr handle);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsIconic(IntPtr handle);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsZoomed(IntPtr handle);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern int GetWindowTextLength(IntPtr handle);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern int GetWindowText(
            IntPtr handle,
            StringBuilder text,
            int maximumCount);

        [DllImport("user32.dll")]
        internal static extern uint GetWindowThreadProcessId(
            IntPtr handle,
            out uint processId);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetWindowPlacement(
            IntPtr handle,
            ref WindowPlacement placement);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetWindowRect(
            IntPtr handle,
            out NativeRect rect);

        [DllImport("user32.dll")]
        internal static extern IntPtr MonitorFromWindow(IntPtr handle, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetMonitorInfo(
            IntPtr monitor,
            ref MonitorInfo info);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetWindowPos(
            IntPtr handle,
            IntPtr insertAfter,
            int x,
            int y,
            int width,
            int height,
            uint flags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ShowWindow(IntPtr handle, int command);

        [DllImport("dwmapi.dll")]
        internal static extern int DwmGetWindowAttribute(
            IntPtr handle,
            int attribute,
            out NativeRect value,
            int size);
    }
}
