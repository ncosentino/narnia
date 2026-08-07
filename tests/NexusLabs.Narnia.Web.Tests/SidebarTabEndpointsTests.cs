using System.Net;
using System.Net.Http.Json;
using NexusLabs.Narnia.Core.Models;

namespace NexusLabs.Narnia.Web.Tests;

public sealed class SidebarTabEndpointsTests
{
    private const string Cwd = @"C:\dev\nexus-labs\genesis";
    private const string SessionA = "11111111-1111-4111-8111-111111111111";
    private const string SessionB = "22222222-2222-4222-8222-222222222222";

    private static readonly DateTimeOffset Now =
        new(2026, 8, 7, 4, 30, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task GetSidebarTabs_ReturnsWorkspaceTabLists()
    {
        using var factory = new NarniaWebAppFactory();
        factory.SidebarTabs
            .Setup(service => service.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<CopilotSidebarWorkspace>)[Workspace()]);
        var client = factory.CreateClient();

        var response = await client.GetFromJsonAsync<CopilotSidebarWorkspace[]>(
            "/api/sidebar-tabs",
            Ct);

        var workspace = Assert.Single(response!);
        Assert.Equal(Cwd, workspace.Cwd);
        Assert.Equal(2, workspace.TabCount);
        Assert.Equal(1, workspace.LiveTabCount);
        Assert.True(workspace.HasLiveRuntime);
    }

    [Fact]
    public async Task RepairSidebarTabs_ClearsEveryTabWhenNoSessionsAreNamed()
    {
        using var factory = new NarniaWebAppFactory();
        factory.SidebarTabs
            .Setup(service => service.ResetAsync(Cwd, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CopilotSidebarRepairResult(
                Cwd, true, [SessionA, SessionB], 0, @"C:\backup.json", null));
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/sidebar-tabs/repair",
            new { cwd = Cwd, sessionIds = (string[]?)null, force = false },
            Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<CopilotSidebarRepairResult>(Ct);
        Assert.True(result!.Succeeded);
        Assert.Equal(0, result.RemainingTabCount);
        factory.SidebarTabs.Verify(
            service => service.ResetAsync(Cwd, false, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RepairSidebarTabs_RemovesOnlyTheNamedSessions()
    {
        using var factory = new NarniaWebAppFactory();
        factory.SidebarTabs
            .Setup(service => service.RemoveTabsAsync(
                Cwd,
                It.Is<IReadOnlyCollection<string>>(ids => ids.Single() == SessionA),
                false,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CopilotSidebarRepairResult(
                Cwd, true, [SessionA], 1, @"C:\backup.json", null));
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/sidebar-tabs/repair",
            new { cwd = Cwd, sessionIds = new[] { SessionA }, force = false },
            Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        factory.SidebarTabs.Verify(
            service => service.ResetAsync(
                It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// A refusal is a conflict rather than a failure: the caller can retry with force once the
    /// live sessions are closed, and the client surfaces the reason instead of silently retrying.
    /// </summary>
    [Fact]
    public async Task RepairSidebarTabs_ReportsRefusalAsConflict()
    {
        using var factory = new NarniaWebAppFactory();
        factory.SidebarTabs
            .Setup(service => service.ResetAsync(Cwd, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CopilotSidebarRepairResult(
                Cwd, false, [], 0, null, "Copilot is still running 1 session(s) in this folder."));
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/sidebar-tabs/repair",
            new { cwd = Cwd, sessionIds = (string[]?)null, force = false },
            Ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<CopilotSidebarRepairResult>(Ct);
        Assert.False(result!.Succeeded);
        Assert.Contains("still running", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RepairSidebarTabs_RejectsAMissingWorkingDirectory()
    {
        using var factory = new NarniaWebAppFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/sidebar-tabs/repair",
            new { cwd = "", sessionIds = (string[]?)null, force = false },
            Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// The sidebar endpoints expose local workspace paths and can rewrite Copilot state, so they
    /// carry the same DNS-rebinding guard as the other local-only surfaces.
    /// </summary>
    [Fact]
    public async Task SidebarTabs_RejectNonLoopbackHostHeaders()
    {
        using var factory = new NarniaWebAppFactory();
        var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/sidebar-tabs");
        request.Headers.Host = "evil.example.com";

        var response = await client.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static CopilotSidebarWorkspace Workspace() =>
        new(
            Cwd,
            $@"C:\copilot\sidebar-sessions-state\hash.json",
            "hash.json",
            1,
            [
                new CopilotSidebarTab(SessionA, 0, true, "First", "ncosentino/narnia", false, 4096),
                new CopilotSidebarTab(SessionB, 1, true, "Second", "ncosentino/narnia", true, null),
            ],
            true,
            Now,
            null);
}
