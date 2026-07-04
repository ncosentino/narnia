using NexusLabs.Narnia.Core.Services;

namespace NexusLabs.Narnia.Core.Tests;

public sealed class ScheduledJobLauncherScriptTests
{
    [Fact]
    public void Build_UsesGivenHostExecutableAndScriptPath()
    {
        var vbs = ScheduledJobLauncherScript.Build("pwsh.exe", @"C:\narnia\abc\run.ps1");

        Assert.Contains("pwsh.exe -NoProfile -ExecutionPolicy Bypass -File", vbs);
        Assert.Contains(@"C:\narnia\abc\run.ps1", vbs);
    }

    [Fact]
    public void Build_RunsHiddenAndWaitsForExit()
    {
        // Window style 0 = SW_HIDE (never shown, not even briefly); bWaitOnReturn = True blocks
        // until the wrapper exits, so Task Scheduler's last-run-result stays meaningful.
        var vbs = ScheduledJobLauncherScript.Build("powershell.exe", @"C:\s\run.ps1");

        Assert.Contains("shell.Run(", vbs);
        Assert.Contains(", 0, True)", vbs);
    }

    [Fact]
    public void Build_ForwardsChildExitCode()
    {
        var vbs = ScheduledJobLauncherScript.Build("powershell.exe", @"C:\s\run.ps1");

        Assert.Contains("exitCode = shell.Run(", vbs);
        Assert.Contains("WScript.Quit exitCode", vbs);
    }

    [Fact]
    public void Build_QuotesScriptPathSoSpacesAreOneArgument()
    {
        var vbs = ScheduledJobLauncherScript.Build("pwsh.exe", @"C:\Program Files\narnia\run.ps1");

        // Doubled quotes are VBScript's own escaping for a literal '"' inside a string literal —
        // the command line the child process actually sees has single quotes around the path.
        Assert.Contains("-File \"\"C:\\Program Files\\narnia\\run.ps1\"\"", vbs);
    }

    [Fact]
    public void Build_DoublesEmbeddedQuotesForVbScriptStringLiteral()
    {
        // The whole command line is embedded as a single VBScript string literal, so any literal
        // '"' within it (here, the ones this method adds around the script path) must be doubled
        // per VBScript syntax or the generated .vbs would not parse.
        var vbs = ScheduledJobLauncherScript.Build("pwsh.exe", @"C:\s\run.ps1");

        Assert.Contains("-File \"\"C:\\s\\run.ps1\"\"", vbs);
    }
}
