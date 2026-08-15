using System.Net.Http.Json;
using NexusLabs.Narnia.Core.Models;

namespace NexusLabs.Narnia.Web.Tests;

public sealed class ProcessDiagnosticsEndpointsTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 29, 5, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task GetProcesses_ReturnsLiveDiagnosticSnapshot()
    {
        using var factory = new NarniaWebAppFactory();
        factory.ProcessDiagnostics
            .Setup(service => service.GetSnapshotAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Snapshot());
        var client = factory.CreateClient();

        var response = await client.GetFromJsonAsync<ProcessDiagnosticsSnapshot>(
            "/api/processes",
            Ct);

        Assert.NotNull(response);
        Assert.True(response!.IsAvailable);
        Assert.Equal(59524, Assert.Single(Assert.Single(response.Terminals).Runtimes).CopilotProcessId);
        Assert.Equal("19afb9f5-6753-42ac-8493-cd34aac79df3",
            Assert.Single(Assert.Single(response.Terminals).Runtimes).Sessions.Single().SessionId);
    }

    [Fact]
    public async Task ProcessesPage_RendersPidSessionAndChildProcess()
    {
        using var factory = new NarniaWebAppFactory();
        factory.ProcessDiagnostics
            .Setup(service => service.GetSnapshotAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Snapshot());
        var client = factory.CreateClient();

        var html = await client.GetStringAsync("/runtime/processes?q=59524", Ct);

        Assert.Contains("PitCrew release", html, StringComparison.Ordinal);
        Assert.Contains("PID 59524", html, StringComparison.Ordinal);
        Assert.Contains("PID 4712", html, StringComparison.Ordinal);
        Assert.Contains("analytics-mcp.exe", html, StringComparison.Ordinal);
        Assert.Contains("C:\\dev\\nexus-labs\\pitcrew", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessesPage_UnsupportedProviderExplainsUnavailableState()
    {
        using var factory = new NarniaWebAppFactory();
        var client = factory.CreateClient();

        var html = await client.GetStringAsync("/runtime/processes", Ct);

        Assert.Contains("Process diagnostics unavailable", html, StringComparison.Ordinal);
        Assert.Contains("Diagnostics are disabled in tests.", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetProcesses_DeepProcessTreeSerializes()
    {
        using var factory = new NarniaWebAppFactory();
        var snapshot = Snapshot();
        var deepTree = BuildDeepTree(40);
        var runtime = Assert.Single(Assert.Single(snapshot.Terminals).Runtimes) with
        {
            RuntimeTree = deepTree,
        };
        var terminal = Assert.Single(snapshot.Terminals) with
        {
            ProcessTree = deepTree,
            Runtimes = [runtime],
        };
        factory.ProcessDiagnostics
            .Setup(service => service.GetSnapshotAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot with { Terminals = [terminal] });
        var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/processes", Ct);

        response.EnsureSuccessStatusCode();
    }

    [Theory]
    [InlineData("/runtime/processes")]
    [InlineData("/api/processes")]
    public async Task ProcessDiagnostics_RejectsNonLoopbackHost(string path)
    {
        using var factory = new NarniaWebAppFactory();
        var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Host = "attacker.example";

        using var response = await client.SendAsync(request, Ct);

        Assert.Equal(System.Net.HttpStatusCode.Forbidden, response.StatusCode);
        factory.ProcessDiagnostics.Verify(
            service => service.GetSnapshotAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static ProcessDiagnosticsSnapshot Snapshot()
    {
        var childOwn = Usage(7.5, 1, 180);
        var child = new ProcessTreeNode(
            4212,
            59524,
            "analytics-mcp.exe",
            Now.AddMinutes(-10),
            childOwn,
            childOwn,
            []);
        var copilotOwn = Usage(12.5, 1, 500);
        var copilotTree = new ProcessTreeNode(
            59524,
            47936,
            "copilot.exe",
            Now.AddHours(-8),
            copilotOwn,
            Usage(20, 2, 680),
            [child]);
        var terminalOwn = Usage(0.1, 1, 80);
        var terminalTree = new ProcessTreeNode(
            4712,
            64824,
            "WindowsTerminal.exe",
            Now.AddHours(-8),
            terminalOwn,
            Usage(22, 5, 900),
            [copilotTree]);
        var runtime = new CopilotRuntimeDiagnostics(
            59524,
            30152,
            4712,
            Now.AddHours(-8),
            [
                new ProcessDescriptor(
                    30152,
                    4712,
                    "pwsh.exe",
                    Now.AddHours(-8),
                    Usage(0.2, 1, 70)),
                new ProcessDescriptor(
                    47936,
                    30152,
                    "node.exe",
                    Now.AddHours(-8),
                    Usage(0.2, 1, 70)),
            ],
            copilotTree,
            [
                new ProcessSessionReference(
                    "19afb9f5-6753-42ac-8493-cd34aac79df3",
                    "PitCrew release",
                    "ncosentino/pitcrew",
                    "main",
                    @"C:\dev\nexus-labs\pitcrew",
                    true),
            ]);
        var terminal = new TerminalProcessDiagnostics(
            4712,
            Now.AddHours(-8),
            terminalTree,
            Usage(2, 3, 220),
            [runtime]);

        return new ProcessDiagnosticsSnapshot(
            true,
            null,
            Now,
            3,
            16,
            "topology",
            "process-tree",
            ["4712:64824:0", "59524:47936:0", "4212:59524:0"],
            Usage(48, 20, 8_000),
            copilotTree.TreeUsage,
            terminalTree.TreeUsage,
            1,
            [terminal],
            []);
    }

    private static ProcessUsage Usage(
        double cpuPercent,
        int processCount,
        long privateMegabytes) =>
        new(
            cpuPercent,
            processCount,
            processCount,
            privateMegabytes * 1024 * 1024,
            privateMegabytes * 1024 * 1024);

    private static ProcessTreeNode BuildDeepTree(int depth)
    {
        var usage = Usage(0, 1, 1);
        var node = new ProcessTreeNode(
            10_000 + depth,
            0,
            "leaf.exe",
            Now,
            usage,
            usage,
            []);
        for (var index = depth - 1; index >= 0; index--)
        {
            var treeUsage = Usage(0, depth - index + 1, depth - index + 1);
            node = new ProcessTreeNode(
                10_000 + index,
                index == 0 ? 0 : 9_999 + index,
                $"process-{index}.exe",
                Now,
                usage,
                treeUsage,
                [node]);
        }

        return node;
    }
}
