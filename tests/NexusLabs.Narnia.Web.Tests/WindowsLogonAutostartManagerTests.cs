using System.IO.Abstractions;
using System.Runtime.Versioning;

namespace NexusLabs.Narnia.Web.Tests;

[SupportedOSPlatform("windows")]
public sealed class WindowsLogonAutostartManagerTests : IDisposable
{
    private readonly string _localAppData = Path.Combine(
        Path.GetTempPath(),
        $"narnia_autostart_{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_localAppData))
            Directory.Delete(_localAppData, recursive: true);
    }

    [Fact]
    public void Enable_RegistersWscriptAndCreatesHiddenLauncherOutsideAppDirectory()
    {
        string? runValue = null;
        var manager = CreateManager(() => runValue, value => runValue = value);
        var executablePath = CreatePublishedExecutable();

        manager.Enable();

        var launcherPath = Path.Combine(_localAppData, "narnia", "start-server-hidden.vbs");
        Assert.Equal($"wscript.exe //B //Nologo \"{launcherPath}\"", runValue);
        Assert.True(File.Exists(launcherPath));
        Assert.False(launcherPath.StartsWith(
            Path.GetDirectoryName(executablePath)!,
            StringComparison.OrdinalIgnoreCase));

        var launcher = File.ReadAllText(launcherPath);
        var launcherBytes = File.ReadAllBytes(launcherPath);
        Assert.Equal(0xFF, launcherBytes[0]);
        Assert.Equal(0xFE, launcherBytes[1]);
        Assert.Contains($"\"\"{executablePath}\"\" --urls http://127.0.0.1:5244", launcher);
        Assert.Contains(
            $"shell.CurrentDirectory = \"{Path.GetDirectoryName(executablePath)}\"",
            launcher);
        Assert.Contains(", 0, False)", launcher);
    }

    [Fact]
    public void EnsureConfigured_ReplacesLegacyDirectExecutableEntry()
    {
        var executablePath = CreatePublishedExecutable();
        string? runValue = $"\"{executablePath}\" --urls http://127.0.0.1:5244";
        var manager = CreateManager(() => runValue, value => runValue = value);

        manager.EnsureConfigured();

        Assert.StartsWith("wscript.exe //B //Nologo", runValue, StringComparison.Ordinal);
    }

    [Fact]
    public void EnsureConfigured_WhenDisabled_DoesNotCreateLauncher()
    {
        string? runValue = null;
        var manager = CreateManager(() => runValue, value => runValue = value);

        manager.EnsureConfigured();

        Assert.Null(runValue);
        Assert.False(File.Exists(Path.Combine(
            _localAppData,
            "narnia",
            "start-server-hidden.vbs")));
    }

    [Fact]
    public void Disable_RemovesRunEntryAndGeneratedLauncher()
    {
        string? runValue = null;
        var manager = CreateManager(() => runValue, value => runValue = value);
        CreatePublishedExecutable();
        manager.Enable();

        manager.Disable();

        Assert.Null(runValue);
        Assert.False(File.Exists(Path.Combine(
            _localAppData,
            "narnia",
            "start-server-hidden.vbs")));
    }

    [Fact]
    public void Enable_WhenPublishedServerIsMissing_FailsWithoutRunEntry()
    {
        string? runValue = null;
        var manager = CreateManager(() => runValue, value => runValue = value);

        Assert.Throws<InvalidOperationException>(() => manager.Enable());
        Assert.Null(runValue);
    }

    [Fact]
    public void Enable_WhenRegistryWriteFails_RemovesGeneratedLauncher()
    {
        var manager = CreateManager(
            readRunValue: () => null,
            writeRunValue: _ => throw new InvalidOperationException("registry unavailable"));
        CreatePublishedExecutable();

        Assert.Throws<InvalidOperationException>(() => manager.Enable());
        Assert.False(File.Exists(Path.Combine(
            _localAppData,
            "narnia",
            "start-server-hidden.vbs")));
    }

    [Fact]
    public void Enable_WhenRepairWriteFails_RestoresExistingLauncher()
    {
        var launcherPath = Path.Combine(
            _localAppData,
            "narnia",
            "start-server-hidden.vbs");
        Directory.CreateDirectory(Path.GetDirectoryName(launcherPath)!);
        File.WriteAllText(launcherPath, "existing launcher");
        var manager = CreateManager(
            readRunValue: () => $"wscript.exe \"{launcherPath}\"",
            writeRunValue: _ => throw new InvalidOperationException("registry unavailable"));
        CreatePublishedExecutable();

        Assert.Throws<InvalidOperationException>(() => manager.Enable());
        Assert.Equal("existing launcher", File.ReadAllText(launcherPath));
    }

    [Fact]
    public void ConcurrentMutations_AreSerialized()
    {
        var concurrentWrites = 0;
        var maxConcurrentWrites = 0;
        string? runValue = "legacy";
        var manager = CreateManager(
            readRunValue: () => runValue,
            writeRunValue: value =>
            {
                var concurrent = Interlocked.Increment(ref concurrentWrites);
                maxConcurrentWrites = Math.Max(maxConcurrentWrites, concurrent);
                Thread.Sleep(50);
                runValue = value;
                Interlocked.Decrement(ref concurrentWrites);
            });
        CreatePublishedExecutable();

        Parallel.Invoke(manager.Enable, manager.Disable);

        Assert.Equal(1, maxConcurrentWrites);
    }

    private WindowsLogonAutostartManager CreateManager(
        Func<string?> readRunValue,
        Action<string?> writeRunValue) =>
        new(
            new FileSystem(),
            _localAppData,
            readRunValue,
            writeRunValue,
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
}
