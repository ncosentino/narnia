using System.IO.Abstractions;
using System.Runtime.Versioning;
using NexusLabs.Narnia.Core.Services;

namespace NexusLabs.Narnia.Web.Tests;

[SupportedOSPlatform("windows")]
public sealed class WindowsLogonAutostartManagerTests : IDisposable
{
    private const string UserId = @"EXAMPLE\tester";

    private readonly string _localAppData = Path.Combine(
        Path.GetTempPath(),
        $"narnia_autostart_{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_localAppData))
            Directory.Delete(_localAppData, recursive: true);
    }

    [Fact]
    public void Enable_RegistersLogonTaskAndCreatesHiddenLauncherOutsideAppDirectory()
    {
        var scheduler = new FakeScheduler();
        var manager = CreateManager(scheduler);
        var executablePath = CreatePublishedExecutable();

        manager.Enable();

        var launcherPath = Path.Combine(_localAppData, "narnia", "start-server-hidden.vbs");
        Assert.True(scheduler.Exists);
        Assert.True(File.Exists(launcherPath));
        Assert.False(launcherPath.StartsWith(
            Path.GetDirectoryName(executablePath)!,
            StringComparison.OrdinalIgnoreCase));

        var script = Assert.Single(scheduler.RegisterScripts);
        Assert.Contains("New-ScheduledTaskTrigger -AtLogOn", script, StringComparison.Ordinal);
        Assert.Contains($"-User '{UserId}'", script, StringComparison.Ordinal);
        Assert.Contains($"-TaskPath '{LogonAutostartTask.Folder}'", script, StringComparison.Ordinal);
        Assert.Contains($"-TaskName '{LogonAutostartTask.Name}'", script, StringComparison.Ordinal);
        Assert.Contains("wscript.exe", script, StringComparison.Ordinal);
        Assert.Contains(launcherPath, script, StringComparison.Ordinal);
    }

    [Fact]
    public void Enable_WritesDeploymentAwareLaunchersThatWaitForTheServer()
    {
        var scheduler = new FakeScheduler();
        var manager = CreateManager(scheduler);
        var executablePath = CreatePublishedExecutable();

        manager.Enable();

        var hiddenLauncherPath = Path.Combine(
            _localAppData,
            "narnia",
            "start-server-hidden.vbs");
        var serverLauncherPath = Path.Combine(
            _localAppData,
            "narnia",
            "start-server.ps1");
        foreach (var launcherPath in new[] { hiddenLauncherPath, serverLauncherPath })
        {
            var launcherBytes = File.ReadAllBytes(launcherPath);
            Assert.Equal(0xFF, launcherBytes[0]);
            Assert.Equal(0xFE, launcherBytes[1]);
        }

        var hiddenLauncher = File.ReadAllText(hiddenLauncherPath);
        Assert.Contains("powershell.exe", hiddenLauncher, StringComparison.Ordinal);
        Assert.Contains(serverLauncherPath, hiddenLauncher, StringComparison.Ordinal);
        Assert.Contains(
            $"shell.CurrentDirectory = \"{Path.GetDirectoryName(executablePath)}\"",
            hiddenLauncher);
        // Waiting keeps the task reported as Running so a dead server is visible in Task Scheduler.
        Assert.Contains(", 0, True)", hiddenLauncher);

        var serverLauncher = File.ReadAllText(serverLauncherPath);
        Assert.Contains("$frameworkDependent", serverLauncher, StringComparison.Ordinal);
        Assert.Contains("Get-Command dotnet.exe", serverLauncher, StringComparison.Ordinal);
        Assert.Contains("Invoke-WebRequest", serverLauncher, StringComparison.Ordinal);
        Assert.Contains(executablePath, serverLauncher, StringComparison.Ordinal);
        Assert.Contains(
            Path.ChangeExtension(executablePath, ".dll"),
            serverLauncher,
            StringComparison.Ordinal);
        Assert.Contains("autostart.log", serverLauncher, StringComparison.Ordinal);
    }

    [Fact]
    public void Enable_ServerLauncherIsValidWindowsPowerShell()
    {
        var scheduler = new FakeScheduler();
        var manager = CreateManager(scheduler);
        CreatePublishedExecutable();
        manager.Enable();
        var serverLauncher = File.ReadAllText(Path.Combine(
            _localAppData,
            "narnia",
            "start-server.ps1"));
        var startInfo = new System.Diagnostics.ProcessStartInfo("powershell.exe")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add(
            "[void][scriptblock]::Create([Console]::In.ReadToEnd())");
        using var process = System.Diagnostics.Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start powershell.exe.");
        process.StandardInput.Write(serverLauncher);
        process.StandardInput.Close();
        var standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.True(process.ExitCode == 0, standardError);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ServerLauncher_SelectsThePublishedDeploymentHost(bool frameworkDependent)
    {
        var appDirectory = Path.Combine(_localAppData, "launcher mode", "app");
        var fakeBin = Path.Combine(_localAppData, "launcher mode", "bin");
        var executablePath = Path.Combine(appDirectory, "NexusLabs.Narnia.Web.exe");
        var assemblyPath = Path.Combine(appDirectory, "NexusLabs.Narnia.Web.dll");
        var runtimeConfigPath = Path.Combine(
            appDirectory,
            "NexusLabs.Narnia.Web.runtimeconfig.json");
        var startupLogPath = Path.Combine(
            _localAppData,
            "launcher mode",
            "autostart.log");
        Directory.CreateDirectory(appDirectory);
        Directory.CreateDirectory(fakeBin);
        File.Copy(
            Path.Combine(Environment.SystemDirectory, "where.exe"),
            executablePath);
        File.Copy(
            Path.Combine(Environment.SystemDirectory, "where.exe"),
            Path.Combine(fakeBin, "dotnet.exe"));
        File.WriteAllText(assemblyPath, "");
        File.WriteAllText(
            runtimeConfigPath,
            frameworkDependent
                ? """{"runtimeOptions":{"frameworks":[{"name":"Microsoft.NETCore.App","version":"10.0.0"}]}}"""
                : """{"runtimeOptions":{}}""");
        var launcher = WindowsLogonAutostartManager.BuildServerLauncher(
            executablePath,
            assemblyPath,
            runtimeConfigPath,
            startupLogPath,
            "http://127.0.0.1:1");
        var launcherPath = Path.Combine(
            _localAppData,
            "launcher mode",
            "start-server.ps1");
        File.WriteAllText(launcherPath, launcher, System.Text.Encoding.Unicode);

        var startInfo = new System.Diagnostics.ProcessStartInfo(
            Path.Combine(
                Environment.SystemDirectory,
                "WindowsPowerShell",
                "v1.0",
                "powershell.exe"))
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = appDirectory,
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(launcherPath);
        startInfo.Environment["PATH"] = fakeBin;
        using var process = System.Diagnostics.Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start powershell.exe.");
        Assert.True(process.WaitForExit(15_000), "The generated launcher did not exit.");
        var standardError = process.StandardError.ReadToEnd();
        var log = File.ReadAllText(startupLogPath);

        Assert.Contains(
            frameworkDependent ? "framework-dependent" : "self-contained",
            log,
            StringComparison.Ordinal);
        Assert.Contains(
            frameworkDependent
                ? $"Host: {Path.Combine(fakeBin, "dotnet.exe")} {assemblyPath}"
                : $"Host: {executablePath}",
            log,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("autostart failed", log, StringComparison.OrdinalIgnoreCase);
        Assert.True(process.HasExited, standardError);
    }

    [Fact]
    public void EnsureConfigured_MigratesLegacyRunEntryToScheduledTask()
    {
        var executablePath = CreatePublishedExecutable();
        var scheduler = new FakeScheduler();
        string? legacyRunValue = $"wscript.exe //B //Nologo \"{executablePath}\"";
        var manager = CreateManager(
            scheduler,
            () => legacyRunValue,
            () => legacyRunValue = null);

        manager.EnsureConfigured();

        Assert.True(scheduler.Exists);
        Assert.Null(legacyRunValue);
    }

    [Fact]
    public void EnsureConfigured_WhenDisabled_DoesNotRegisterTaskOrCreateLauncher()
    {
        var scheduler = new FakeScheduler();
        var manager = CreateManager(scheduler);

        manager.EnsureConfigured();

        Assert.False(scheduler.Exists);
        Assert.Empty(scheduler.RegisterScripts);
        Assert.False(File.Exists(Path.Combine(
            _localAppData,
            "narnia",
            "start-server-hidden.vbs")));
        Assert.False(File.Exists(Path.Combine(
            _localAppData,
            "narnia",
            "start-server.ps1")));
    }

    [Fact]
    public void IsEnabled_ReportsEnabledWhileOnlyTheLegacyRunEntryRemains()
    {
        var scheduler = new FakeScheduler();
        string? legacyRunValue = "wscript.exe //B //Nologo \"legacy.vbs\"";
        var manager = CreateManager(scheduler, () => legacyRunValue, () => legacyRunValue = null);

        Assert.True(manager.IsEnabled());
    }

    [Fact]
    public void Disable_RemovesTaskLegacyEntryAndGeneratedLauncher()
    {
        var scheduler = new FakeScheduler();
        string? legacyRunValue = "wscript.exe //B //Nologo \"legacy.vbs\"";
        var manager = CreateManager(scheduler, () => legacyRunValue, () => legacyRunValue = null);
        CreatePublishedExecutable();
        manager.Enable();

        manager.Disable();

        Assert.False(scheduler.Exists);
        Assert.Null(legacyRunValue);
        Assert.False(manager.IsEnabled());
        Assert.False(File.Exists(Path.Combine(
            _localAppData,
            "narnia",
            "start-server-hidden.vbs")));
    }

    [Fact]
    public void Enable_WhenPublishedServerIsMissing_FailsWithoutRegisteringTask()
    {
        var scheduler = new FakeScheduler();
        var manager = CreateManager(scheduler);

        Assert.Throws<InvalidOperationException>(() => manager.Enable());
        Assert.False(scheduler.Exists);
    }

    [Fact]
    public void Enable_WhenPublishedDeploymentIsIncomplete_FailsWithoutRegisteringTask()
    {
        var scheduler = new FakeScheduler();
        var manager = CreateManager(scheduler);
        var executablePath = Path.Combine(
            _localAppData,
            "narnia",
            "app",
            "NexusLabs.Narnia.Web.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(executablePath)!);
        File.WriteAllText(executablePath, "");

        var error = Assert.Throws<InvalidOperationException>(() => manager.Enable());

        Assert.Contains("incomplete", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(scheduler.Exists);
    }

    [Fact]
    public void Enable_WhenTaskRegistrationFails_RemovesGeneratedLauncherAndKeepsLegacyEntry()
    {
        var scheduler = new FakeScheduler { FailRegistration = true };
        string? legacyRunValue = "wscript.exe //B //Nologo \"legacy.vbs\"";
        var manager = CreateManager(scheduler, () => legacyRunValue, () => legacyRunValue = null);
        CreatePublishedExecutable();

        Assert.Throws<InvalidOperationException>(() => manager.Enable());
        Assert.False(File.Exists(Path.Combine(
            _localAppData,
            "narnia",
            "start-server-hidden.vbs")));
        Assert.False(File.Exists(Path.Combine(
            _localAppData,
            "narnia",
            "start-server.ps1")));
        // The legacy entry is the only remaining autostart, so a failed migration must not clear it.
        Assert.NotNull(legacyRunValue);
    }

    [Fact]
    public void Enable_WhenTaskRegistrationFails_RestoresExistingLauncher()
    {
        var hiddenLauncherPath = Path.Combine(
            _localAppData,
            "narnia",
            "start-server-hidden.vbs");
        var serverLauncherPath = Path.Combine(
            _localAppData,
            "narnia",
            "start-server.ps1");
        Directory.CreateDirectory(Path.GetDirectoryName(hiddenLauncherPath)!);
        File.WriteAllText(hiddenLauncherPath, "existing hidden launcher");
        File.WriteAllText(serverLauncherPath, "existing server launcher");
        var scheduler = new FakeScheduler { FailRegistration = true };
        var manager = CreateManager(scheduler);
        CreatePublishedExecutable();

        Assert.Throws<InvalidOperationException>(() => manager.Enable());
        Assert.Equal("existing hidden launcher", File.ReadAllText(hiddenLauncherPath));
        Assert.Equal("existing server launcher", File.ReadAllText(serverLauncherPath));
    }

    [Fact]
    public void ConcurrentMutations_AreSerialized()
    {
        var concurrentMutations = 0;
        var maxConcurrentMutations = 0;
        var scheduler = new FakeScheduler
        {
            OnMutate = () =>
            {
                var concurrent = Interlocked.Increment(ref concurrentMutations);
                maxConcurrentMutations = Math.Max(maxConcurrentMutations, concurrent);
                Thread.Sleep(50);
                Interlocked.Decrement(ref concurrentMutations);
            },
        };
        var manager = CreateManager(scheduler);
        CreatePublishedExecutable();

        Parallel.Invoke(manager.Enable, manager.Disable);

        Assert.Equal(1, maxConcurrentMutations);
    }

    private WindowsLogonAutostartManager CreateManager(
        FakeScheduler scheduler,
        Func<string?>? readLegacyRunValue = null,
        Action? clearLegacyRunValue = null) =>
        new(
            new FileSystem(),
            _localAppData,
            UserId,
            () => scheduler.Exists,
            scheduler.Register,
            scheduler.Remove,
            readLegacyRunValue ?? (() => null),
            clearLegacyRunValue ?? (() => { }),
            $@"Local\Narnia.Autostart.Tests.{Guid.NewGuid():N}");

    private string CreatePublishedExecutable()
    {
        var path = Path.Combine(
            _localAppData,
            "narnia",
            "app",
            "NexusLabs.Narnia.Web.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "");
        File.WriteAllText(Path.ChangeExtension(path, ".dll"), "");
        File.WriteAllText(
            Path.ChangeExtension(path, ".runtimeconfig.json"),
            """
            {
              "runtimeOptions": {
                "frameworks": [
                  {
                    "name": "Microsoft.NETCore.App",
                    "version": "10.0.0"
                  }
                ]
              }
            }
            """);
        return path;
    }

    private sealed class FakeScheduler
    {
        public bool Exists { get; private set; }

        public bool FailRegistration { get; init; }

        public Action? OnMutate { get; init; }

        public List<string> RegisterScripts { get; } = [];

        public void Register(string script)
        {
            OnMutate?.Invoke();
            if (FailRegistration)
                throw new InvalidOperationException("scheduler unavailable");
            RegisterScripts.Add(script);
            Exists = true;
        }

        public void Remove()
        {
            OnMutate?.Invoke();
            Exists = false;
        }
    }
}
