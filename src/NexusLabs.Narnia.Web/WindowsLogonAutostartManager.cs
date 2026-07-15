using System.IO.Abstractions;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;
using NexusLabs.Narnia.Core.Services;

namespace NexusLabs.Narnia.Web;

/// <summary>
/// Manages per-user Windows logon autostart through <c>HKCU\...\CurrentVersion\Run</c>. The Run
/// entry invokes <c>wscript.exe</c>, which launches the published Narnia server through a generated
/// hidden VBScript shim so no console window is created or briefly flashed.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsLogonAutostartManager : ILogonAutostartManager
{
    private const string RunSubKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Narnia";
    private const string ServerUrl = "http://127.0.0.1:5244";
    private readonly IFileSystem _fileSystem;
    private readonly string _localAppData;
    private readonly Func<string?> _readRunValue;
    private readonly Action<string?> _writeRunValue;
    private readonly string _mutationMutexName;

    /// <summary>
    /// Creates the Windows autostart manager using the current user's LocalAppData and registry.
    /// </summary>
    /// <param name="fileSystem">Filesystem used to maintain the generated hidden launcher.</param>
    public WindowsLogonAutostartManager(IFileSystem fileSystem)
        : this(
            fileSystem,
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ReadRunValue,
            WriteRunValue,
            BuildMutationMutexName())
    {
    }

    internal WindowsLogonAutostartManager(
        IFileSystem fileSystem,
        string localAppData,
        Func<string?> readRunValue,
        Action<string?> writeRunValue,
        string mutationMutexName)
    {
        _fileSystem = fileSystem;
        _localAppData = localAppData;
        _readRunValue = readRunValue;
        _writeRunValue = writeRunValue;
        _mutationMutexName = mutationMutexName;
    }

    /// <inheritdoc />
    public bool IsSupported => true;

    /// <inheritdoc />
    public bool IsEnabled() => _readRunValue() is not null;

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
            if (_readRunValue() is not null)
                Configure();
        });
    }

    /// <inheritdoc />
    public void Disable()
    {
        ExecuteExclusive(() =>
        {
            _writeRunValue(null);
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
        var launcher = HiddenProcessLauncherScript.Build(
            commandLine,
            workingDirectory,
            waitForExit: false);

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
            _writeRunValue($"wscript.exe //B //Nologo \"{launcherPath}\"");
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

    private static string? ReadRunValue()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunSubKey, writable: false);
        return key?.GetValue(
            ValueName,
            defaultValue: null,
            RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
    }

    private static void WriteRunValue(string? value)
    {
        if (value is null)
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunSubKey, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
            return;
        }

        using var writableKey = Registry.CurrentUser.CreateSubKey(RunSubKey, writable: true);
        writableKey.SetValue(ValueName, value, RegistryValueKind.String);
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
    public void EnsureConfigured()
    {
    }

    /// <inheritdoc />
    public void Disable()
    {
    }
}
