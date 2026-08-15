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
    private const string HiddenLauncherFileName = "start-server-hidden.vbs";
    private const string ServerLauncherFileName = "start-server.ps1";
    private const string StartupLogFileName = "autostart.log";
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
            DeleteGeneratedFile(GetHiddenLauncherPath());
            DeleteGeneratedFile(GetServerLauncherPath());
        });
    }

    private void Configure()
    {
        var executablePath = ResolveServerExecutable();
        var workingDirectory = _fileSystem.Path.GetDirectoryName(executablePath)!;
        var assemblyPath = ResolvePublishedFile(
            workingDirectory,
            "NexusLabs.Narnia.Web.dll");
        var runtimeConfigPath = ResolvePublishedFile(
            workingDirectory,
            "NexusLabs.Narnia.Web.runtimeconfig.json");
        var hiddenLauncherPath = GetHiddenLauncherPath();
        var serverLauncherPath = GetServerLauncherPath();
        var launcherDirectory = _fileSystem.Path.GetDirectoryName(hiddenLauncherPath)!;
        var startupLogPath = _fileSystem.Path.Combine(
            _localAppData,
            "narnia",
            "logs",
            StartupLogFileName);
        var serverLauncher = BuildServerLauncher(
            executablePath,
            assemblyPath,
            runtimeConfigPath,
            startupLogPath);
        var commandLine =
            $"\"powershell.exe\" -NoProfile -NonInteractive -ExecutionPolicy Bypass " +
            $"-File \"{serverLauncherPath}\"";

        // Waiting for the server keeps the task reported as Running for its lifetime and records
        // the real exit code, which is the signal the previous Run entry could never provide.
        var hiddenLauncher = HiddenProcessLauncherScript.Build(
            commandLine,
            workingDirectory,
            waitForExit: true);

        _fileSystem.Directory.CreateDirectory(launcherDirectory);
        var previousHiddenLauncher = _fileSystem.File.Exists(hiddenLauncherPath)
            ? _fileSystem.File.ReadAllText(hiddenLauncherPath)
            : null;
        var previousServerLauncher = _fileSystem.File.Exists(serverLauncherPath)
            ? _fileSystem.File.ReadAllText(serverLauncherPath)
            : null;

        try
        {
            AtomicTextFile.Write(
                _fileSystem,
                serverLauncherPath,
                serverLauncher,
                Encoding.Unicode);
            AtomicTextFile.Write(
                _fileSystem,
                hiddenLauncherPath,
                hiddenLauncher,
                Encoding.Unicode);
            _registerTask(LogonAutostartTask.BuildRegisterScript(
                _userId,
                "wscript.exe",
                $"//B //Nologo \"{hiddenLauncherPath}\"",
                workingDirectory));
        }
        catch
        {
            RestoreGeneratedFile(hiddenLauncherPath, previousHiddenLauncher);
            RestoreGeneratedFile(serverLauncherPath, previousServerLauncher);
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

    private string ResolvePublishedFile(string workingDirectory, string fileName)
    {
        var path = _fileSystem.Path.Combine(workingDirectory, fileName);
        if (!_fileSystem.File.Exists(path))
        {
            throw new InvalidOperationException(
                $"The published Narnia server is incomplete: '{fileName}' was not found.");
        }

        return path;
    }

    private string GetHiddenLauncherPath() =>
        _fileSystem.Path.Combine(_localAppData, "narnia", HiddenLauncherFileName);

    private string GetServerLauncherPath() =>
        _fileSystem.Path.Combine(_localAppData, "narnia", ServerLauncherFileName);

    private void DeleteGeneratedFile(string path)
    {
        if (_fileSystem.File.Exists(path))
            _fileSystem.File.Delete(path);
    }

    private void RestoreGeneratedFile(string path, string? previousContent)
    {
        if (previousContent is null)
        {
            DeleteGeneratedFile(path);
            return;
        }

        AtomicTextFile.Write(
            _fileSystem,
            path,
            previousContent,
            Encoding.Unicode);
    }

    private static string BuildServerLauncher(
        string executablePath,
        string assemblyPath,
        string runtimeConfigPath,
        string startupLogPath) =>
        $$"""
        # Auto-generated by Narnia. Do not edit this file.
        $ErrorActionPreference = 'Stop'
        $executablePath = '{{EscapePowerShellLiteral(executablePath)}}'
        $assemblyPath = '{{EscapePowerShellLiteral(assemblyPath)}}'
        $runtimeConfigPath = '{{EscapePowerShellLiteral(runtimeConfigPath)}}'
        $startupLogPath = '{{EscapePowerShellLiteral(startupLogPath)}}'
        $serverUrl = '{{ServerUrl}}'

        try {
            if ((Invoke-WebRequest "$serverUrl/health" -UseBasicParsing -TimeoutSec 2).StatusCode -eq 200) {
                exit 0
            }
        }
        catch {
        }

        try {
            $runtimeOptions = (Get-Content -LiteralPath $runtimeConfigPath -Raw | ConvertFrom-Json).runtimeOptions
            $frameworkDependent =
                $null -ne $runtimeOptions.framework -or
                $null -ne $runtimeOptions.frameworks
            $deploymentKind = if ($frameworkDependent) { 'framework-dependent' } else { 'self-contained' }
            $logDirectory = Split-Path -Parent $startupLogPath
            New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
            "[$(Get-Date -Format o)] Starting Narnia ($deploymentKind)." |
                Set-Content -LiteralPath $startupLogPath -Encoding UTF8

            if ($frameworkDependent) {
                $dotnetCommand = Get-Command dotnet.exe -ErrorAction SilentlyContinue
                if ($null -ne $dotnetCommand) {
                    $dotnetPath = $dotnetCommand.Source
                }
                else {
                    $dotnetPath = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
                    if (-not (Test-Path -LiteralPath $dotnetPath -PathType Leaf)) {
                        throw 'A framework-dependent Narnia deployment requires dotnet.exe.'
                    }
                }

                & $dotnetPath $assemblyPath '--urls' $serverUrl *>> $startupLogPath
            }
            else {
                & $executablePath '--urls' $serverUrl *>> $startupLogPath
            }

            $exitCode = $LASTEXITCODE
            "[$(Get-Date -Format o)] Narnia exited with code $exitCode." |
                Add-Content -LiteralPath $startupLogPath -Encoding UTF8
            exit $exitCode
        }
        catch {
            $logDirectory = Split-Path -Parent $startupLogPath
            New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
            "[$(Get-Date -Format o)] Narnia autostart failed.`r`n$($_ | Out-String)" |
                Add-Content -LiteralPath $startupLogPath -Encoding UTF8
            exit 1
        }
        """;

    private static string EscapePowerShellLiteral(string value) =>
        value.Replace("'", "''");

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
