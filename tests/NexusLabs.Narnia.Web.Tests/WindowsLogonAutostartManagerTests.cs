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
    public void Enable_WritesUnicodeLauncherThatWaitsForTheServer()
    {
        var scheduler = new FakeScheduler();
        var manager = CreateManager(scheduler);
        var executablePath = CreatePublishedExecutable();

        manager.Enable();

        var launcherPath = Path.Combine(_localAppData, "narnia", "start-server-hidden.vbs");
        var launcherBytes = File.ReadAllBytes(launcherPath);
        Assert.Equal(0xFF, launcherBytes[0]);
        Assert.Equal(0xFE, launcherBytes[1]);

        var launcher = File.ReadAllText(launcherPath);
        Assert.Contains($"\"\"{executablePath}\"\" --urls http://127.0.0.1:5244", launcher);
        Assert.Contains(
            $"shell.CurrentDirectory = \"{Path.GetDirectoryName(executablePath)}\"",
            launcher);
        // Waiting keeps the task reported as Running so a dead server is visible in Task Scheduler.
        Assert.Contains(", 0, True)", launcher);
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
        // The legacy entry is the only remaining autostart, so a failed migration must not clear it.
        Assert.NotNull(legacyRunValue);
    }

    [Fact]
    public void Enable_WhenTaskRegistrationFails_RestoresExistingLauncher()
    {
        var launcherPath = Path.Combine(
            _localAppData,
            "narnia",
            "start-server-hidden.vbs");
        Directory.CreateDirectory(Path.GetDirectoryName(launcherPath)!);
        File.WriteAllText(launcherPath, "existing launcher");
        var scheduler = new FakeScheduler { FailRegistration = true };
        var manager = CreateManager(scheduler);
        CreatePublishedExecutable();

        Assert.Throws<InvalidOperationException>(() => manager.Enable());
        Assert.Equal("existing launcher", File.ReadAllText(launcherPath));
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
