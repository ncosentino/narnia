using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;
using NexusLabs.Narnia.Core.Models;
using NexusLabs.Narnia.Core.Services;

namespace NexusLabs.Narnia.Web;

/// <summary>
/// Writes Windows Task Scheduler entries through the built-in <c>ScheduledTasks</c> PowerShell
/// module, shelling out to <c>powershell.exe</c>. Registration uses the exact script Narnia also
/// shows for copy-and-paste, so the two setup modes are identical. User tasks register under the
/// interactive account without elevation.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsScheduledTaskRegistrar : IScheduledTaskRegistrar
{
    /// <inheritdoc />
    public bool IsSupported => true;

    /// <inheritdoc />
    public ValueTask<ScheduledTaskCommandResult> RegisterAsync(
        ScheduledTaskRegistration reg, CancellationToken ct = default) =>
        RunAsync(ScheduledTaskRegistrationScript.Build(reg), ct);

    /// <inheritdoc />
    public ValueTask<ScheduledTaskCommandResult> SetEnabledAsync(
        string folder, string name, bool enabled, CancellationToken ct = default)
    {
        var verb = enabled ? "Enable-ScheduledTask" : "Disable-ScheduledTask";
        return RunAsync($"{verb} -TaskName '{Esc(name)}' -TaskPath '{Esc(folder)}'", ct);
    }

    /// <inheritdoc />
    public ValueTask<ScheduledTaskCommandResult> RunAsync(
        string folder, string name, CancellationToken ct = default) =>
        RunAsync($"Start-ScheduledTask -TaskName '{Esc(name)}' -TaskPath '{Esc(folder)}'", ct);

    /// <inheritdoc />
    public ValueTask<ScheduledTaskCommandResult> DeleteAsync(
        string folder, string name, CancellationToken ct = default) =>
        RunAsync(
            "$task = Get-ScheduledTask -TaskName '" + Esc(name) +
            "' -TaskPath '" + Esc(folder) +
            "' -ErrorAction SilentlyContinue; if ($null -ne $task) { " +
            "Unregister-ScheduledTask -TaskName '" + Esc(name) +
            "' -TaskPath '" + Esc(folder) +
            "' -Confirm:$false }",
            ct);

    private static async ValueTask<ScheduledTaskCommandResult> RunAsync(string script, CancellationToken ct)
    {
        var psi = new ProcessStartInfo("powershell.exe")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-NonInteractive");
        psi.ArgumentList.Add("-Command");
        psi.ArgumentList.Add("$ErrorActionPreference='Stop'; " + script);

        using var process = Process.Start(psi);
        if (process is null)
            return ScheduledTaskCommandResult.Fail("Could not start powershell.exe");

        var stderr = await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        return process.ExitCode == 0
            ? ScheduledTaskCommandResult.Success
            : ScheduledTaskCommandResult.Fail(string.IsNullOrWhiteSpace(stderr) ? $"exit {process.ExitCode}" : stderr.Trim());
    }

    private static string Esc(string value) => value.Replace("'", "''");
}

/// <summary>No-op registrar for platforms without OS scheduler write support.</summary>
public sealed class UnsupportedScheduledTaskRegistrar : IScheduledTaskRegistrar
{
    /// <inheritdoc />
    public bool IsSupported => false;

    private static ValueTask<ScheduledTaskCommandResult> Unsupported() =>
        ValueTask.FromResult(ScheduledTaskCommandResult.Fail("Registering tasks is not supported on this platform."));

    /// <inheritdoc />
    public ValueTask<ScheduledTaskCommandResult> RegisterAsync(ScheduledTaskRegistration reg, CancellationToken ct = default) => Unsupported();

    /// <inheritdoc />
    public ValueTask<ScheduledTaskCommandResult> SetEnabledAsync(string folder, string name, bool enabled, CancellationToken ct = default) => Unsupported();

    /// <inheritdoc />
    public ValueTask<ScheduledTaskCommandResult> RunAsync(string folder, string name, CancellationToken ct = default) => Unsupported();

    /// <inheritdoc />
    public ValueTask<ScheduledTaskCommandResult> DeleteAsync(string folder, string name, CancellationToken ct = default) => Unsupported();
}
