using NexusLabs.Narnia.Core.Services;

namespace NexusLabs.Narnia.Core.Tests;

public sealed class TerminalCommandBuilderTests
{
    private const string SessionId = "6e9968ed-d767-4d53-98f9-48b49fd01911";
    private const string ShellPath = @"C:\Program Files\PowerShell\7\pwsh.exe";

    private readonly TerminalCommandBuilder _builder = new();

    [Theory]
    [InlineData("pwsh")]
    [InlineData("powershell")]
    public void BuildShellArguments_PowerShell_UsesNoExitCommand(string shellName)
    {
        var args = _builder.BuildShellArguments(shellName, SessionId, "copilot");

        Assert.Equal($"-NoExit -Command \"copilot --resume={SessionId}\"", args);
    }

    [Fact]
    public void BuildShellArguments_Cmd_UsesSlashK()
    {
        var args = _builder.BuildShellArguments("cmd", SessionId, "copilot");

        Assert.Equal($"/k copilot --resume={SessionId}", args);
    }

    [Fact]
    public void BuildShellArguments_OtherShell_UsesPosixForm()
    {
        var args = _builder.BuildShellArguments("bash", SessionId, "copilot");

        Assert.Equal($"-c \"copilot --resume={SessionId}; exec $SHELL\"", args);
    }

    [Fact]
    public void BuildShellArguments_WrappedCopilotCommand_PrependsWrapper()
    {
        // A multi-word command (e.g. Microsoft's "Agency" wrapper) is embedded verbatim as source
        // text for a freshly-launched shell, so "agency copilot" is parsed as "agency" (the
        // executable) with "copilot" as its first argument -- no special handling needed here.
        var args = _builder.BuildShellArguments("pwsh", SessionId, "agency copilot");

        Assert.Equal($"-NoExit -Command \"agency copilot --resume={SessionId}\"", args);
    }

    [Fact]
    public void BuildNewTabSegment_WithDirectory_IncludesStartingDirectory()
    {
        var segment = _builder.BuildNewTabSegment(
            ShellPath, "pwsh", new TerminalLaunchTab(SessionId, "My Tab", @"C:\dev\project"), "copilot");

        Assert.StartsWith("new-tab --title \"My Tab\" --suppressApplicationTitle ", segment);
        Assert.Contains("--startingDirectory \"C:\\dev\\project\"", segment);
        Assert.Contains($"-- \"{ShellPath}\" -NoExit -Command \"copilot --resume={SessionId}\"", segment);
    }

    [Fact]
    public void BuildNewTabSegment_WithoutDirectory_OmitsStartingDirectory()
    {
        var segment = _builder.BuildNewTabSegment(
            ShellPath, "pwsh", new TerminalLaunchTab(SessionId, "My Tab", null), "copilot");

        Assert.DoesNotContain("--startingDirectory", segment);
    }

    [Fact]
    public void BuildNewTabSegment_TitleWithQuotes_IsEscaped()
    {
        var segment = _builder.BuildNewTabSegment(
            ShellPath, "pwsh", new TerminalLaunchTab(SessionId, "He said \"hi\"", null), "copilot");

        Assert.Contains("--title \"He said \\\"hi\\\"\"", segment);
    }

    [Fact]
    public void BuildWindowCommand_MultipleTabs_JoinedWithSeparator()
    {
        var tabs = new[]
        {
            new TerminalLaunchTab("11111111-1111-4111-8111-111111111111", "One", @"C:\one"),
            new TerminalLaunchTab("22222222-2222-4222-8222-222222222222", "Two", @"C:\two"),
        };

        var command = _builder.BuildWindowCommand(ShellPath, "pwsh", tabs, "copilot");

        var segments = command.Split(" ; ");
        Assert.Equal(2, segments.Length);
        Assert.Contains("--title \"One\"", segments[0]);
        Assert.Contains("--resume=11111111-1111-4111-8111-111111111111", segments[0]);
        Assert.Contains("--title \"Two\"", segments[1]);
        Assert.Contains("--resume=22222222-2222-4222-8222-222222222222", segments[1]);
    }

    [Theory]
    [InlineData("pwsh")]
    [InlineData("powershell")]
    public void BuildDirectLaunchArguments_PowerShell_SetsTitleAndResumes(string shellName)
    {
        var args = _builder.BuildDirectLaunchArguments(shellName, new TerminalLaunchTab(SessionId, "My Tab", null), "copilot");

        Assert.Contains("$host.UI.RawUI.WindowTitle = 'My Tab'", args);
        Assert.Contains($"copilot --resume={SessionId}", args);
    }

    [Fact]
    public void BuildDirectLaunchArguments_Cmd_UsesTitleAndSlashK()
    {
        var args = _builder.BuildDirectLaunchArguments("cmd", new TerminalLaunchTab(SessionId, "My Tab", null), "copilot");

        Assert.Equal($"/k title My Tab & copilot --resume={SessionId}", args);
    }

    [Fact]
    public void BuildDirectLaunchArguments_OtherShell_UsesPosixTitleEscape()
    {
        var args = _builder.BuildDirectLaunchArguments("bash", new TerminalLaunchTab(SessionId, "My Tab", null), "copilot");

        Assert.Contains("printf '\\033]0;My Tab\\007'", args);
        Assert.Contains($"copilot --resume={SessionId}; exec $SHELL", args);
    }

    [Fact]
    public void BuildDirectLaunchArguments_WrappedCopilotCommand_PrependsWrapper()
    {
        var args = _builder.BuildDirectLaunchArguments("cmd", new TerminalLaunchTab(SessionId, "My Tab", null), "agency copilot");

        Assert.Equal($"/k title My Tab & agency copilot --resume={SessionId}", args);
    }

    [Fact]
    public void FindWindowsTerminalPath_DoesNotThrow_AndReturnsNullOrWtPath()
    {
        var path = _builder.FindWindowsTerminalPath();

        if (path is not null)
            Assert.EndsWith("wt.exe", path);
    }
}
