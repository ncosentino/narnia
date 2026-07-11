using NexusLabs.Narnia.Core.Services;

namespace NexusLabs.Narnia.Core.Tests;

public sealed class TerminalLauncherTests
{
    private const string WtPath = @"C:\wt.exe";
    private const string ShellPath = @"C:\pwsh.exe";
    private const string CopilotCommand = "copilot";

    private readonly Mock<ITerminalCommandBuilder> _commandBuilder = new();
    private readonly Mock<IProcessLauncher> _processLauncher = new();

    private TerminalLauncher Launcher() => new(_commandBuilder.Object, _processLauncher.Object);

    private static TerminalLaunchTab Tab(string sessionId, string? directory = null) =>
        new(sessionId, $"Title {sessionId}", directory);

    [Fact]
    public void Launch_NoTabs_DoesNothing()
    {
        var outcome = Launcher().Launch(ShellPath, "pwsh", [], TerminalWindowMode.SingleWindow, CopilotCommand);

        Assert.Empty(outcome.LaunchedSessionIds);
        Assert.Empty(outcome.Failures);
        _processLauncher.Verify(p => p.Start(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public void Launch_WindowsTerminalSingleWindow_StartsOneJoinedCommand()
    {
        _commandBuilder.Setup(b => b.FindWindowsTerminalPath()).Returns(WtPath);
        var tabs = new[] { Tab("s1"), Tab("s2") };
        _commandBuilder.Setup(b => b.BuildWindowCommand(ShellPath, "pwsh", tabs, CopilotCommand)).Returns("joined-command");

        var outcome = Launcher().Launch(ShellPath, "pwsh", tabs, TerminalWindowMode.SingleWindow, CopilotCommand);

        _processLauncher.Verify(p => p.Start(WtPath, "joined-command", null), Times.Once);
        Assert.Equal(new[] { "s1", "s2" }, outcome.LaunchedSessionIds);
        Assert.Empty(outcome.Failures);
    }

    [Fact]
    public void Launch_WindowsTerminalSeparateWindows_StartsOnePerTab()
    {
        _commandBuilder.Setup(b => b.FindWindowsTerminalPath()).Returns(WtPath);
        _commandBuilder.Setup(b => b.BuildNewTabSegment(ShellPath, "pwsh", It.IsAny<TerminalLaunchTab>(), CopilotCommand))
            .Returns<string, string, TerminalLaunchTab, string>((_, _, tab, _) => $"segment-{tab.SessionId}");

        var outcome = Launcher().Launch(ShellPath, "pwsh", [Tab("s1"), Tab("s2")], TerminalWindowMode.SeparateWindows, CopilotCommand);

        _processLauncher.Verify(p => p.Start(WtPath, "segment-s1", null), Times.Once);
        _processLauncher.Verify(p => p.Start(WtPath, "segment-s2", null), Times.Once);
        Assert.Equal(new[] { "s1", "s2" }, outcome.LaunchedSessionIds);
    }

    [Fact]
    public void Launch_NoWindowsTerminal_FallsBackToDirectShellPerTab_WithDirectory()
    {
        _commandBuilder.Setup(b => b.FindWindowsTerminalPath()).Returns((string?)null);
        _commandBuilder.Setup(b => b.BuildDirectLaunchArguments("pwsh", It.IsAny<TerminalLaunchTab>(), CopilotCommand))
            .Returns<string, TerminalLaunchTab, string>((_, tab, _) => $"direct-{tab.SessionId}");

        var outcome = Launcher().Launch(
            ShellPath, "pwsh", [Tab("s1", @"C:\one"), Tab("s2")], TerminalWindowMode.SingleWindow, CopilotCommand);

        // Even SingleWindow mode degrades to per-tab when there is no Windows Terminal.
        _processLauncher.Verify(p => p.Start(ShellPath, "direct-s1", @"C:\one"), Times.Once);
        _processLauncher.Verify(p => p.Start(ShellPath, "direct-s2", null), Times.Once);
        Assert.Equal(new[] { "s1", "s2" }, outcome.LaunchedSessionIds);
    }

    [Fact]
    public void Launch_SingleWindowStartThrows_AllTabsFail()
    {
        _commandBuilder.Setup(b => b.FindWindowsTerminalPath()).Returns(WtPath);
        _commandBuilder.Setup(b => b.BuildWindowCommand(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<TerminalLaunchTab>>(), It.IsAny<string>()))
            .Returns("cmd");
        _processLauncher.Setup(p => p.Start(WtPath, "cmd", null)).Throws(new InvalidOperationException("boom"));

        var outcome = Launcher().Launch(ShellPath, "pwsh", [Tab("s1"), Tab("s2")], TerminalWindowMode.SingleWindow, CopilotCommand);

        Assert.Empty(outcome.LaunchedSessionIds);
        Assert.Equal(2, outcome.Failures.Count);
        Assert.All(outcome.Failures, f => Assert.Equal("boom", f.Reason));
    }

    [Fact]
    public void Launch_PerTabStartThrowsForOne_OthersStillLaunch()
    {
        _commandBuilder.Setup(b => b.FindWindowsTerminalPath()).Returns(WtPath);
        _commandBuilder.Setup(b => b.BuildNewTabSegment(ShellPath, "pwsh", It.IsAny<TerminalLaunchTab>(), CopilotCommand))
            .Returns<string, string, TerminalLaunchTab, string>((_, _, tab, _) => $"segment-{tab.SessionId}");
        _processLauncher.Setup(p => p.Start(WtPath, "segment-s1", null)).Throws(new InvalidOperationException("nope"));

        var outcome = Launcher().Launch(ShellPath, "pwsh", [Tab("s1"), Tab("s2")], TerminalWindowMode.SeparateWindows, CopilotCommand);

        Assert.Equal(new[] { "s2" }, outcome.LaunchedSessionIds);
        var failure = Assert.Single(outcome.Failures);
        Assert.Equal("s1", failure.SessionId);
        Assert.Equal("nope", failure.Reason);
    }
}
