using System.Net;
using System.Net.Http.Json;
using NexusLabs.Narnia.Core.Models;
using NexusLabs.Narnia.Core.Services;

namespace NexusLabs.Narnia.Web.Tests;

public sealed class WindowsEndpointsTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 22, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static TerminalWindowTab Tab(string sessionId, int order, string? directory = null) =>
        new(sessionId, order, directory);

    [Fact]
    public async Task GetWindows_ReturnsOpenAndClosed_WithSessionEnrichment()
    {
        using var factory = new NarniaWebAppFactory();
        factory.SessionRepository
            .Setup(r => r.GetByIdAsync("sess-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Session("sess-1", @"C:\dev\x", "owner/repo", "main", "My session", null, Now, Now));

        var repo = factory.WindowsRepository;
        await repo.UpsertOpenAsync(100, "key-open", [Tab("sess-1", 0, @"C:\dev\x")], Now, Ct);
        await repo.UpsertOpenAsync(200, "key-closed", [Tab("sess-2", 0)], Now, Ct);
        var closedId = (await repo.GetOpenAsync(Ct)).Single(w => w.TerminalProcessId == 200).Id;
        await repo.CloseAsync(closedId, Now.AddMinutes(1), Ct);

        var client = factory.CreateClient();
        var response = await client.GetFromJsonAsync<WindowsResponse>("/api/windows", Ct);

        Assert.NotNull(response);
        var open = Assert.Single(response!.Open);
        Assert.Equal("open", open.Status);
        var tab = Assert.Single(open.Tabs);
        Assert.Equal("My session", tab.Summary);
        Assert.Equal("owner/repo", tab.Repository);
        Assert.Single(response.Closed);
    }

    [Fact]
    public async Task Reopen_MissingWindow_Returns404()
    {
        using var factory = new NarniaWebAppFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsync($"/api/windows/{Guid.NewGuid()}/reopen", null, Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Reopen_WithoutWindowsTerminal_Returns400_AndNeverBuildsCommand()
    {
        using var factory = new NarniaWebAppFactory();
        var repo = factory.WindowsRepository;
        await repo.UpsertOpenAsync(100, "k", [Tab("sess-1", 0)], Now, Ct);
        var id = (await repo.GetOpenAsync(Ct)).Single().Id;

        var client = factory.CreateClient();
        var response = await client.PostAsync($"/api/windows/{id}/reopen", null, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        factory.CommandBuilder.Verify(
            b => b.BuildWindowCommand(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<TerminalLaunchTab>>()),
            Times.Never);
    }

    [Fact]
    public async Task Name_PersistsNameAndPinsWindow()
    {
        using var factory = new NarniaWebAppFactory();
        var repo = factory.WindowsRepository;
        await repo.UpsertOpenAsync(100, "k", [Tab("sess-1", 0)], Now, Ct);
        var id = (await repo.GetOpenAsync(Ct)).Single().Id;

        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync($"/api/windows/{id}/name", new { name = "My Window" }, Ct);
        response.EnsureSuccessStatusCode();

        var updated = await repo.GetByIdAsync(id, Ct);
        Assert.Equal("My Window", updated!.Name);
        Assert.True(updated.Pinned);
    }

    [Fact]
    public async Task Delete_RemovesWindow()
    {
        using var factory = new NarniaWebAppFactory();
        var repo = factory.WindowsRepository;
        await repo.UpsertOpenAsync(100, "k", [Tab("sess-1", 0)], Now, Ct);
        var id = (await repo.GetOpenAsync(Ct)).Single().Id;

        var client = factory.CreateClient();
        var response = await client.DeleteAsync($"/api/windows/{id}", Ct);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Null(await repo.GetByIdAsync(id, Ct));
    }

    [Fact]
    public async Task GetAutostart_ReportsManagerState()
    {
        using var factory = new NarniaWebAppFactory();
        factory.Autostart.SetupGet(a => a.IsSupported).Returns(true);
        factory.Autostart.Setup(a => a.IsEnabled()).Returns(true);

        var client = factory.CreateClient();
        var response = await client.GetFromJsonAsync<AutostartResponse>("/api/autostart", Ct);

        Assert.True(response!.Supported);
        Assert.True(response.Enabled);
    }

    [Fact]
    public async Task PostAutostart_Enable_CallsManagerEnable()
    {
        using var factory = new NarniaWebAppFactory();
        factory.Autostart.SetupGet(a => a.IsSupported).Returns(true);
        factory.Autostart.Setup(a => a.IsEnabled()).Returns(true);

        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/autostart", new { enabled = true }, Ct);

        response.EnsureSuccessStatusCode();
        factory.Autostart.Verify(a => a.Enable(), Times.Once);
    }

    [Fact]
    public async Task PostAutostart_Unsupported_Returns400_AndDoesNotEnable()
    {
        using var factory = new NarniaWebAppFactory();
        factory.Autostart.SetupGet(a => a.IsSupported).Returns(false);

        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/autostart", new { enabled = true }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        factory.Autostart.Verify(a => a.Enable(), Times.Never);
    }

    private sealed record WindowsResponse(List<WindowDto> Open, List<WindowDto> Closed);

    private sealed record WindowDto(string Id, string Status, List<TabDto> Tabs);

    private sealed record TabDto(string SessionId, string? Summary, string? Repository);

    private sealed record AutostartResponse(bool Supported, bool Enabled);
}
