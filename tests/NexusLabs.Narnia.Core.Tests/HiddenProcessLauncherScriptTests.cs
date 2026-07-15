using NexusLabs.Narnia.Core.Services;

namespace NexusLabs.Narnia.Core.Tests;

public sealed class HiddenProcessLauncherScriptTests
{
    [Fact]
    public void Build_RunsHiddenWithoutWaiting()
    {
        var script = HiddenProcessLauncherScript.Build(
            "\"C:\\narnia\\server.exe\" --urls http://127.0.0.1:5244",
            @"C:\narnia",
            waitForExit: false);

        Assert.Contains("shell.Run(", script);
        Assert.Contains(", 0, False)", script);
        Assert.Contains("shell.CurrentDirectory = \"C:\\narnia\"", script);
    }

    [Fact]
    public void Build_WaitsAndForwardsExitCode()
    {
        var script = HiddenProcessLauncherScript.Build(
            "pwsh.exe -File \"C:\\narnia\\run.ps1\"",
            workingDirectory: null,
            waitForExit: true);

        Assert.Contains(", 0, True)", script);
        Assert.Contains("WScript.Quit exitCode", script);
    }

    [Fact]
    public void Build_EscapesEmbeddedQuotesForVbScript()
    {
        var script = HiddenProcessLauncherScript.Build(
            "\"C:\\Program Files\\narnia.exe\" --urls http://127.0.0.1:5244",
            workingDirectory: null,
            waitForExit: false);

        Assert.Contains("\"\"C:\\Program Files\\narnia.exe\"\"", script);
    }
}
