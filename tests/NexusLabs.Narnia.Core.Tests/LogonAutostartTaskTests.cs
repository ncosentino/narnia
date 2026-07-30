using NexusLabs.Narnia.Core.Services;

namespace NexusLabs.Narnia.Core.Tests;

public sealed class LogonAutostartTaskTests
{
    private const string UserId = @"EXAMPLE\tester";

    [Fact]
    public void Folder_IsSeparateFromTheCatalogFolderSoTasksAreNotReportedAsOrphaned()
    {
        // The Schedules page reports every task in \Narnia\ without a catalog entry as orphaned.
        Assert.NotEqual(@"\Narnia\", LogonAutostartTask.Folder);
        Assert.StartsWith(@"\Narnia\", LogonAutostartTask.Folder, StringComparison.Ordinal);
        Assert.EndsWith(@"\", LogonAutostartTask.Folder, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildRegisterScript_UsesLogonTriggerBoundToTheUser()
    {
        var script = LogonAutostartTask.BuildRegisterScript(
            UserId,
            "wscript.exe",
            "//B //Nologo \"C:\\narnia\\start.vbs\"",
            @"C:\narnia\app");

        Assert.Contains("New-ScheduledTaskTrigger -AtLogOn", script, StringComparison.Ordinal);
        Assert.Contains($"-User '{UserId}'", script, StringComparison.Ordinal);
        Assert.Contains($"-UserId '{UserId}'", script, StringComparison.Ordinal);
        Assert.Contains("-LogonType Interactive", script, StringComparison.Ordinal);
        Assert.Contains("-RunLevel Limited", script, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildRegisterScript_KeepsALongRunningServerAliveAndUnique()
    {
        var script = LogonAutostartTask.BuildRegisterScript(
            UserId,
            "wscript.exe",
            "//B //Nologo \"C:\\narnia\\start.vbs\"",
            @"C:\narnia\app");

        Assert.Contains("-ExecutionTimeLimit ([TimeSpan]::Zero)", script, StringComparison.Ordinal);
        Assert.Contains("-MultipleInstances IgnoreNew", script, StringComparison.Ordinal);
        Assert.Contains("-AllowStartIfOnBatteries", script, StringComparison.Ordinal);
        Assert.Contains("-DontStopIfGoingOnBatteries", script, StringComparison.Ordinal);
        Assert.Contains("-RestartCount 3", script, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildRegisterScript_AppliesTheLogonDelay()
    {
        var script = LogonAutostartTask.BuildRegisterScript(
            UserId,
            "wscript.exe",
            "//B //Nologo \"C:\\narnia\\start.vbs\"",
            @"C:\narnia\app");

        Assert.Contains(
            $"$trigger.Delay = 'PT{(int)LogonAutostartTask.LogonDelay.TotalSeconds}S'",
            script,
            StringComparison.Ordinal);
    }

    [Fact]
    public void BuildRegisterScript_StampsTheRecognitionMarker()
    {
        var script = LogonAutostartTask.BuildRegisterScript(
            UserId,
            "wscript.exe",
            "//B //Nologo \"C:\\narnia\\start.vbs\"",
            @"C:\narnia\app");

        Assert.Contains($"-Description '{LogonAutostartTask.Marker}'", script, StringComparison.Ordinal);
        Assert.Contains("-Force", script, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildRegisterScript_EscapesSingleQuotesInEveryInterpolatedValue()
    {
        var script = LogonAutostartTask.BuildRegisterScript(
            "EXAMPLE\\o'brien",
            "wscript.exe",
            "//B //Nologo \"C:\\nar'nia\\start.vbs\"",
            @"C:\nar'nia\app");

        Assert.Contains("EXAMPLE\\o''brien", script, StringComparison.Ordinal);
        Assert.Contains(@"C:\nar''nia\app", script, StringComparison.Ordinal);
        Assert.DoesNotContain("o'brien", script, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildRegisterScript_OmitsWorkingDirectoryWhenNotProvided()
    {
        var script = LogonAutostartTask.BuildRegisterScript(
            UserId,
            "wscript.exe",
            "//B //Nologo \"C:\\narnia\\start.vbs\"",
            "");

        Assert.DoesNotContain("-WorkingDirectory", script, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BuildRegisterScript_RejectsMissingUser(string userId)
    {
        Assert.Throws<ArgumentException>(() => LogonAutostartTask.BuildRegisterScript(
            userId,
            "wscript.exe",
            "//B",
            @"C:\narnia\app"));
    }

    [Fact]
    public void BuildExistsScript_AndRemoveScript_TargetTheSameTask()
    {
        var exists = LogonAutostartTask.BuildExistsScript();
        var remove = LogonAutostartTask.BuildRemoveScript();

        foreach (var script in new[] { exists, remove })
        {
            Assert.Contains($"-TaskName '{LogonAutostartTask.Name}'", script, StringComparison.Ordinal);
            Assert.Contains($"-TaskPath '{LogonAutostartTask.Folder}'", script, StringComparison.Ordinal);
        }

        Assert.Contains("Unregister-ScheduledTask", remove, StringComparison.Ordinal);
        // Removing an absent task must stay a no-op rather than a failure.
        Assert.Contains("SilentlyContinue", remove, StringComparison.Ordinal);
    }
}
