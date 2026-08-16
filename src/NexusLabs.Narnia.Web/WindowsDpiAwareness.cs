using System.Runtime.InteropServices;

namespace NexusLabs.Narnia.Web;

/// <summary>Configures Win32 geometry APIs to use physical per-monitor coordinates.</summary>
internal static class WindowsDpiAwareness
{
    private const int ErrorAccessDenied = 5;
    private static readonly IntPtr PerMonitorAwareV2 = new(-4);

    public static void EnablePerMonitorV2()
    {
        if (!OperatingSystem.IsWindows())
            return;
        if (SetProcessDpiAwarenessContext(PerMonitorAwareV2))
            return;

        var error = Marshal.GetLastWin32Error();
        if (error != ErrorAccessDenied)
        {
            throw new InvalidOperationException(
                $"Could not enable per-monitor DPI awareness. Win32 error: {error}.");
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr value);
}
