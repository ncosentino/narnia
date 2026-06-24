using NexusLabs.Narnia.Core.Models;
using NexusLabs.Narnia.Core.Repositories;
using NexusLabs.Narnia.Core.Services;

namespace NexusLabs.Narnia.Core.Tests;

public sealed class LiveWindowDetectorTests
{
    private const string TerminalName = "WindowsTerminal.exe";

    private static ILiveWindowDetector Detector(params ProcessRecord[] processes)
    {
        var provider = new Mock<IProcessSnapshotProvider>();
        provider.Setup(p => p.GetProcesses()).Returns(processes);
        return new LiveWindowDetector(provider.Object);
    }

    private static ProcessRecord Pwsh(int pid, int parent, string sessionId) =>
        new(pid, parent, "pwsh.exe", $"\"pwsh.exe\" -NoExit -Command \"copilot --resume={sessionId}\"");

    [Fact]
    public void DetectWindows_SingleWindowSingleTab_MapsSessionId()
    {
        const string sessionId = "6e9968ed-d767-4d53-98f9-48b49fd01911";
        var detector = Detector(
            new ProcessRecord(100, 1, TerminalName, "wt.exe"),
            Pwsh(200, 100, sessionId));

        var windows = detector.DetectWindows();

        var window = Assert.Single(windows);
        Assert.Equal(100, window.TerminalProcessId);
        var tab = Assert.Single(window.Tabs);
        Assert.Equal(sessionId, tab.SessionId);
        Assert.Equal(0, tab.Order);
    }

    [Fact]
    public void DetectWindows_UppercaseGuid_IsLowercased()
    {
        const string upper = "6E9968ED-D767-4D53-98F9-48B49FD01911";
        var detector = Detector(
            new ProcessRecord(100, 1, TerminalName, "wt.exe"),
            Pwsh(200, 100, upper));

        var tab = Assert.Single(Assert.Single(detector.DetectWindows()).Tabs);

        Assert.Equal(upper.ToLowerInvariant(), tab.SessionId);
    }

    [Fact]
    public void DetectWindows_MultipleWindows_GroupedByTerminalAndSortedByPid()
    {
        var detector = Detector(
            new ProcessRecord(300, 1, TerminalName, "wt.exe"),
            new ProcessRecord(100, 1, TerminalName, "wt.exe"),
            Pwsh(310, 300, "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"),
            Pwsh(110, 100, "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb"));

        var windows = detector.DetectWindows();

        Assert.Equal(2, windows.Count);
        Assert.Equal(100, windows[0].TerminalProcessId);
        Assert.Equal(300, windows[1].TerminalProcessId);
    }

    [Fact]
    public void DetectWindows_NonResumeProcessesAndCopilotFreeWindow_AreIgnored()
    {
        var detector = Detector(
            // A terminal window whose only tab is a plain shell (no copilot) — must be ignored.
            new ProcessRecord(100, 1, TerminalName, "wt.exe"),
            new ProcessRecord(200, 100, "pwsh.exe", "\"pwsh.exe\" -NoExit"),
            // An unrelated process mentioning nothing relevant.
            new ProcessRecord(900, 1, "node.exe", "node server.js"));

        Assert.Empty(detector.DetectWindows());
    }

    [Fact]
    public void DetectWindows_SameSessionAcrossShellNodeCopilot_DedupedToOneTab()
    {
        const string sessionId = "8ce93c39-a0a7-4559-9f1f-12cb716d4da9";
        var detector = Detector(
            new ProcessRecord(100, 1, TerminalName, "wt.exe"),
            Pwsh(200, 100, sessionId),
            new ProcessRecord(300, 200, "node.exe", $"node npm-loader.js --resume={sessionId}"),
            new ProcessRecord(400, 300, "copilot.exe", $"copilot.exe --resume={sessionId}"));

        var window = Assert.Single(detector.DetectWindows());

        var tab = Assert.Single(window.Tabs);
        Assert.Equal(sessionId, tab.SessionId);
    }

    [Fact]
    public void DetectWindows_DeepAncestorChain_ResolvesOwningTerminal()
    {
        const string sessionId = "7034d203-7f58-41a1-80b4-7600fb7e6898";
        // Only the deep copilot.exe carries the id; its chain climbs node -> pwsh -> terminal.
        var detector = Detector(
            new ProcessRecord(100, 1, TerminalName, "wt.exe"),
            new ProcessRecord(200, 100, "pwsh.exe", "\"pwsh.exe\" -NoExit"),
            new ProcessRecord(300, 200, "node.exe", "node npm-loader.js"),
            new ProcessRecord(400, 300, "copilot.exe", $"copilot.exe --resume={sessionId}"));

        var window = Assert.Single(detector.DetectWindows());

        Assert.Equal(100, window.TerminalProcessId);
        Assert.Equal(sessionId, Assert.Single(window.Tabs).SessionId);
    }

    [Fact]
    public void DetectWindows_NoTerminalAncestor_IsIgnored()
    {
        var detector = Detector(
            // pwsh carrying a resume id, but parent chain never reaches a terminal.
            Pwsh(200, 1, "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"));

        Assert.Empty(detector.DetectWindows());
    }

    [Fact]
    public void DetectWindows_CapturesTitleAndDirectoryFromLaunchCommand_InOrder()
    {
        const string first = "11111111-1111-4111-8111-111111111111";
        const string second = "22222222-2222-4222-8222-222222222222";
        var launch =
            $"wt.exe new-tab --title \"Tab One\" --suppressApplicationTitle --startingDirectory \"C:\\dev\\one\" -- \"pwsh.exe\" -NoExit -Command \"copilot --resume={first}\" ; " +
            $"new-tab --title \"Tab Two\" --suppressApplicationTitle --startingDirectory \"C:\\dev\\two\" -- \"pwsh.exe\" -NoExit -Command \"copilot --resume={second}\"";

        var detector = Detector(
            new ProcessRecord(100, 1, TerminalName, launch),
            // Children discovered out of order to prove ordering comes from the launch command.
            Pwsh(220, 100, second),
            Pwsh(210, 100, first));

        var window = Assert.Single(detector.DetectWindows());

        Assert.Equal(2, window.Tabs.Count);
        Assert.Equal(first, window.Tabs[0].SessionId);
        Assert.Equal("Tab One", window.Tabs[0].Title);
        Assert.Equal(@"C:\dev\one", window.Tabs[0].Directory);
        Assert.Equal(0, window.Tabs[0].Order);
        Assert.Equal(second, window.Tabs[1].SessionId);
        Assert.Equal("Tab Two", window.Tabs[1].Title);
        Assert.Equal(@"C:\dev\two", window.Tabs[1].Directory);
        Assert.Equal(1, window.Tabs[1].Order);
    }

    [Fact]
    public void DetectWindows_TabsNotInLaunchCommand_FollowKnownTabsOrderedById()
    {
        const string launched = "11111111-1111-4111-8111-111111111111";
        const string addedLater = "00000000-0000-4000-8000-000000000000";
        var launch =
            $"wt.exe new-tab --title \"Launched\" --startingDirectory \"C:\\dev\\one\" -- \"pwsh.exe\" -NoExit -Command \"copilot --resume={launched}\"";

        var detector = Detector(
            new ProcessRecord(100, 1, TerminalName, launch),
            Pwsh(210, 100, launched),
            Pwsh(220, 100, addedLater));

        var window = Assert.Single(detector.DetectWindows());

        Assert.Equal(2, window.Tabs.Count);
        // The launch-command tab keeps order 0 with its captured metadata...
        Assert.Equal(launched, window.Tabs[0].SessionId);
        Assert.Equal("Launched", window.Tabs[0].Title);
        // ...and the later, uncaptured tab follows with no metadata.
        Assert.Equal(addedLater, window.Tabs[1].SessionId);
        Assert.Null(window.Tabs[1].Title);
        Assert.Null(window.Tabs[1].Directory);
    }

    [Fact]
    public void DetectWindows_TerminalProcessOwnResumeArg_IsNotCountedAsLiveTab()
    {
        // A WindowsTerminal.exe process keeps the --resume arg it was launched with in its OWN
        // command line forever, even after that tab/window is closed. Only its descendant shell
        // carries a *live* tab. Here the launch session (closed) lives only in the terminal's
        // command line, while a different session is live via a child shell. Only the live one
        // must be detected — otherwise closed sessions never disappear.
        const string closedLaunchSession = "11111111-1111-4111-8111-111111111111";
        const string liveSession = "22222222-2222-4222-8222-222222222222";
        var detector = Detector(
            new ProcessRecord(100, 1, TerminalName,
                $"wt.exe new-tab -- pwsh.exe -NoExit -Command \"copilot --resume={closedLaunchSession}\""),
            Pwsh(200, 100, liveSession));

        var window = Assert.Single(detector.DetectWindows());

        var tab = Assert.Single(window.Tabs);
        Assert.Equal(liveSession, tab.SessionId);
    }

    [Fact]
    public void DetectWindows_NullCommandLines_AreSkippedSafely()
    {
        var detector = Detector(
            new ProcessRecord(100, 1, TerminalName, null),
            new ProcessRecord(200, 100, "pwsh.exe", null));

        Assert.Empty(detector.DetectWindows());
    }
}
