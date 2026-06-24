using System.Diagnostics;
using System.Runtime.Versioning;
using NexusLabs.Narnia.Core.Services;

namespace NexusLabs.Narnia.Web;

/// <summary>
/// Windows logon autostart via the per-user <c>HKCU\…\CurrentVersion\Run</c> key, managed
/// through <c>reg.exe</c> so no registry package or elevation is required. The entry launches
/// the published Narnia server bound to its loopback port; at logon nothing else is running,
/// and the existing run-state/health check keeps later session starts idempotent.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsLogonAutostartManager : ILogonAutostartManager
{
    private const string RunKey = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Narnia";
    private const string ServerUrl = "http://127.0.0.1:5244";

    /// <inheritdoc />
    public bool IsSupported => true;

    /// <inheritdoc />
    public bool IsEnabled() =>
        RunReg("query", RunKey, "/v", ValueName) == 0;

    /// <inheritdoc />
    public void Enable()
    {
        var command = $"\"{ResolveServerExecutable()}\" --urls {ServerUrl}";
        RunReg("add", RunKey, "/v", ValueName, "/t", "REG_SZ", "/d", command, "/f");
    }

    /// <inheritdoc />
    public void Disable()
    {
        if (IsEnabled())
            RunReg("delete", RunKey, "/v", ValueName, "/f");
    }

    private static string ResolveServerExecutable()
    {
        var published = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "narnia", "app", "NexusLabs.Narnia.Web.exe");
        if (File.Exists(published))
            return published;

        return Environment.ProcessPath ?? published;
    }

    private static int RunReg(params string[] arguments)
    {
        var psi = new ProcessStartInfo("reg.exe")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
            psi.ArgumentList.Add(argument);

        using var process = Process.Start(psi);
        if (process is null)
            return -1;

        process.WaitForExit(10_000);
        return process.HasExited ? process.ExitCode : -1;
    }
}

/// <summary>
/// No-op autostart manager for platforms where logon autostart is not supported.
/// </summary>
public sealed class UnsupportedLogonAutostartManager : ILogonAutostartManager
{
    /// <inheritdoc />
    public bool IsSupported => false;

    /// <inheritdoc />
    public bool IsEnabled() => false;

    /// <inheritdoc />
    public void Enable()
    {
    }

    /// <inheritdoc />
    public void Disable()
    {
    }
}
