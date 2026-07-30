using System.Diagnostics;
using System.IO.Abstractions;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;
using NexusLabs.Narnia.Core.Services;

namespace NexusLabs.Narnia.Web;

/// <summary>
/// Manages per-user Windows logon autostart through a Task Scheduler entry that launches the
/// published Narnia server. The task runs a generated hidden VBScript shim so no console window is
/// created or briefly flashed, and Task Scheduler records whether each logon launch actually ran.
/// </summary>
/// <remarks>
/// Earlier versions used an <c>HKCU\...\CurrentVersion\Run</c> value. That mechanism reported
/// nothing when it failed to launch, so a missed autostart was indistinguishable from one that
/// never fired. Any surviving Run value is removed as part of enabling or repairing the task.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class WindowsLogonAutostartManager : ILogonAutostartManager
{
    private const string LegacyRunSubKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string LegacyValueName = "Narnia";
    private const string ServerUrl = "http://127.0.0.1:5244";
    private readonly IFileSystem _fileSystem;
    private readonly string _localAppData;
    private readonly string _userId;
    private readonly Func<bool> _taskExists;
    private readonly Action<string> _registerTask;
    private readonly Action _removeTask;
    private readonly Func<string?> _readLegacyRunValue;
    private readonly Action _clearLegacyRunValue;
    private readonly string _mutationMutexName;

    /// <summary>
    /// Creates the Windows autostart manager using the current user's LocalAppData, identity, and
    /// Task Scheduler.
    /// </summary>
    /// <param name="fileSystem">Filesystem used to maintain the generated hidden launcher.</param>
    public WindowsLogonAutostartManager(IFileSystem fileSystem)
        : this(
            fileSystem,
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ResolveCurrentUserId(),
            TaskExists,
            RegisterTask,
            RemoveTask,
            ReadLegacyRunValue,
            ClearLegacyRunValue,
            BuildMutationMutexName())
    {
    }

    internal WindowsLogonAutostartManager(
        IFileSystem fileSystem,
        string localAppData,
        string userId,
        Func<bool> taskExists,
        Action<string> registerTask,
        Action removeTask,
        Func<string?> readLegacyRunValue,
        Action clearLegacyRunValue,
        string mutationMutexName)
    {
        _fileSystem = fileSystem;
        _localAppData = localAppData;
        _userId = userId;
        _taskExists = taskExists;
        _registerTask = registerTask;
        _removeTask = removeTask;
        _readLegacyRunValue = readLegacyRunValue;
        _clearLegacyRunValue = clearLegacyRunValue;
        _mutationMutexName = mutationMutexName;
    }

    /// <inheritdoc />
    public bool IsSupported => true;

    /// <inheritdoc />
    /// <remarks>
    /// A surviving legacy Run value still counts as enabled so an upgrade repairs it into a task
    /// rather than silently turning the feature off.
    /// </remarks>
    public bool IsEnabled() => _taskExists() || _readLegacyRunValue() is not null;

    /// <inheritdoc />
    public void Enable()
    {
        ExecuteExclusive(Configure);
    }

    /// <inheritdoc />
    public void EnsureConfigured()
    {
        ExecuteExclusive(() =>
        {
            if (_taskExists() || _readLegacyRunValue() is not null)
                Configure();
        });
    }

    /// <inheritdoc />
    public void Disable()
    {
        ExecuteExclusive(() =>
        {
            _removeTask();
            _clearLegacyRunValue();
            var launcherPath = GetLauncherPath();
            if (_fileSystem.File.Exists(launcherPath))
                _fileSystem.File.Delete(launcherPath);
        });
    }

    private void Configure()
    {
        var executablePath = ResolveServerExecutable();
        var workingDirectory = _fileSystem.Path.GetDirectoryName(executablePath)!;
        var launcherPath = GetLauncherPath();
        var launcherDirectory = _fileSystem.Path.GetDirectoryName(launcherPath)!;
        var commandLine = $"\"{executablePath}\" --urls {ServerUrl}";

        // Waiting for the server keeps the task reported as Running for its lifetime and records
        // the real exit code, which is the signal the previous Run entry could never provide.
        var launcher = HiddenProcessLauncherScript.Build(
            commandLine,
            workingDirectory,
            waitForExit: true);

        _fileSystem.Directory.CreateDirectory(launcherDirectory);
        var previousLauncher = _fileSystem.File.Exists(launcherPath)
            ? _fileSystem.File.ReadAllText(launcherPath)
            : null;
        AtomicTextFile.Write(
            _fileSystem,
            launcherPath,
            launcher,
            Encoding.Unicode);

        try
        {
            _registerTask(LogonAutostartTask.BuildRegisterScript(
                _userId,
                "wscript.exe",
                $"//B //Nologo \"{launcherPath}\"",
                workingDirectory));
        }
        catch
        {
            if (previousLauncher is null)
                _fileSystem.File.Delete(launcherPath);
            else
                AtomicTextFile.Write(
                    _fileSystem,
                    launcherPath,
                    previousLauncher,
                    Encoding.Unicode);
            throw;
        }

        // Only retire the legacy entry once the task is registered, so a failed migration never
        // leaves the user with neither mechanism.
        _clearLegacyRunValue();
    }

    private void ExecuteExclusive(Action action)
    {
        using var mutex = new Mutex(initiallyOwned: false, _mutationMutexName);
        var acquired = false;
        try
        {
            try
            {
                acquired = mutex.WaitOne(TimeSpan.FromSeconds(15));
            }
            catch (AbandonedMutexException)
            {
                acquired = true;
            }

            if (!acquired)
            {
                throw new TimeoutException(
                    "Timed out waiting to update the Narnia logon autostart configuration.");
            }

            action();
        }
        finally
        {
            if (acquired)
                mutex.ReleaseMutex();
        }
    }

    private string ResolveServerExecutable()
    {
        var published = _fileSystem.Path.Combine(
            _localAppData,
            "narnia",
            "app",
            "NexusLabs.Narnia.Web.exe");
        if (!_fileSystem.File.Exists(published))
        {
            throw new InvalidOperationException(
                $"The published Narnia server was not found at '{published}'. " +
                "Publish or start Narnia once before enabling logon autostart.");
        }

        return published;
    }

    private string GetLauncherPath() =>
        _fileSystem.Path.Combine(_localAppData, "narnia", "start-server-hidden.vbs");

    private static string BuildMutationMutexName()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var sid = identity.User?.Value
            ?? throw new InvalidOperationException("The current Windows user SID could not be resolved.");
        return $@"Global\Narnia.LogonAutostart.{sid}";
    }

    private static string ResolveCurrentUserId()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return identity.Name;
    }

    private static bool TaskExists()
    {
        var result = RunPowerShell(LogonAutostartTask.BuildExistsScript());
        return result.ExitCode == 0 &&
            result.StandardOutput.Contains("true", StringComparison.OrdinalIgnoreCase);
    }

    private static void RegisterTask(string script)
    {
        var result = RunPowerShell(script);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Could not register the Narnia logon autostart task: {result.Failure}");
        }
    }

    private static void RemoveTask()
    {
        var result = RunPowerShell(LogonAutostartTask.BuildRemoveScript());
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Could not remove the Narnia logon autostart task: {result.Failure}");
        }
    }

    private static PowerShellResult RunPowerShell(string script)
    {
        var startInfo = new ProcessStartInfo("powershell.exe")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add("$ErrorActionPreference='Stop'; " + script);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start powershell.exe.");
        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return new PowerShellResult(
            process.ExitCode,
            standardOutput,
            string.IsNullOrWhiteSpace(standardError)
                ? $"exit {process.ExitCode}"
                : standardError.Trim());
    }

    private static string? ReadLegacyRunValue()
    {
        using var key = Registry.CurrentUser.OpenSubKey(LegacyRunSubKey, writable: false);
        return key?.GetValue(
            LegacyValueName,
            defaultValue: null,
            RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
    }

    private static void ClearLegacyRunValue()
    {
        using var key = Registry.CurrentUser.OpenSubKey(LegacyRunSubKey, writable: true);
        key?.DeleteValue(LegacyValueName, throwOnMissingValue: false);
    }

    private sealed record PowerShellResult(int ExitCode, string StandardOutput, string Failure);
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
    public void EnsureConfigured()
    {
    }

    /// <inheritdoc />
    public void Disable()
    {
    }
}
