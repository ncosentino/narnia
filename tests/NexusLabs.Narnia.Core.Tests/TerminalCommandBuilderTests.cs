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
        var args = _builder.BuildShellArguments(shellName, SessionId);

        Assert.Equal($"-NoExit -Command \"copilot --resume={SessionId}\"", args);
    }

    [Fact]
    public void BuildShellArguments_Cmd_UsesSlashK()
    {
        var args = _builder.BuildShellArguments("cmd", SessionId);

        Assert.Equal($"/k copilot --resume={SessionId}", args);
    }

    [Fact]
    public void BuildShellArguments_OtherShell_UsesPosixForm()
    {
        var args = _builder.BuildShellArguments("bash", SessionId);

        Assert.Equal($"-c \"copilot --resume={SessionId}; exec $SHELL\"", args);
    }

    [Fact]
    public void BuildNewTabSegment_WithDirectory_IncludesStartingDirectory()
    {
        var segment = _builder.BuildNewTabSegment(
            ShellPath, "pwsh", new TerminalLaunchTab(SessionId, "My Tab", @"C:\dev\project"));

        Assert.StartsWith("new-tab --title \"My Tab\" --suppressApplicationTitle ", segment);
        Assert.Contains("--startingDirectory \"C:\\dev\\project\"", segment);
        Assert.Contains($"-- \"{ShellPath}\" -NoExit -Command \"copilot --resume={SessionId}\"", segment);
    }

    [Fact]
    public void BuildNewTabSegment_WithoutDirectory_OmitsStartingDirectory()
    {
        var segment = _builder.BuildNewTabSegment(
            ShellPath, "pwsh", new TerminalLaunchTab(SessionId, "My Tab", null));

        Assert.DoesNotContain("--startingDirectory", segment);
    }

    [Fact]
    public void BuildNewTabSegment_TitleWithQuotes_IsEscaped()
    {
        var segment = _builder.BuildNewTabSegment(
            ShellPath, "pwsh", new TerminalLaunchTab(SessionId, "He said \"hi\"", null));

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

        var command = _builder.BuildWindowCommand(ShellPath, "pwsh", tabs);

        var segments = command.Split(" ; ");
        Assert.Equal(2, segments.Length);
        Assert.Contains("--title \"One\"", segments[0]);
        Assert.Contains("--resume=11111111-1111-4111-8111-111111111111", segments[0]);
        Assert.Contains("--title \"Two\"", segments[1]);
        Assert.Contains("--resume=22222222-2222-4222-8222-222222222222", segments[1]);
    }

    [Fact]
    public void FindWindowsTerminalPath_DoesNotThrow_AndReturnsNullOrWtPath()
    {
        var path = _builder.FindWindowsTerminalPath();

        if (path is not null)
            Assert.EndsWith("wt.exe", path);
    }
}
