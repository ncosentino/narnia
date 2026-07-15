using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using NexusLabs.Narnia.Core.Models;
using NexusLabs.Narnia.Core.Repositories;
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
            .ReturnsAsync(new Session("sess-1", @"C:\dev\x", "owner/repo", "main", "My session", null, Now, Now)
            {
                IsFavorite = true,
            });

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
        Assert.True(tab.IsFavorite);
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
    public async Task Reopen_WithoutWindowsTerminal_FallsBackToDirectLaunch()
    {
        using var factory = new NarniaWebAppFactory();
        // Default CommandBuilder reports no Windows Terminal, so the launcher uses the direct
        // shell fallback instead of erroring — reopen is now consistent with launch/launch-bulk.
        var repo = factory.WindowsRepository;
        await repo.UpsertOpenAsync(100, "k", [Tab("sess-1", 0)], Now, Ct);
        var id = (await repo.GetOpenAsync(Ct)).Single().Id;

        var client = factory.CreateClient();
        var response = await client.PostAsync($"/api/windows/{id}/reopen", null, Ct);

        response.EnsureSuccessStatusCode();
        // The single tab is launched directly (no wt.exe), never as a joined window command.
        factory.ProcessLauncher.Verify(
            p => p.Start(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()),
            Times.Once);
        factory.CommandBuilder.Verify(
            b => b.BuildWindowCommand(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<TerminalLaunchTab>>(), It.IsAny<string>()),
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
    public async Task Startup_RepairsEnabledAutostartConfiguration()
    {
        using var factory = new NarniaWebAppFactory();
        factory.Autostart.SetupGet(a => a.IsSupported).Returns(true);

        var client = factory.CreateClient();
        await client.GetAsync("/health", Ct);

        factory.Autostart.Verify(a => a.EnsureConfigured(), Times.Once);
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

    [Fact]
    public async Task BulkReopen_SelectedSessions_LaunchesEachViaFallback()
    {
        using var factory = new NarniaWebAppFactory();
        await factory.Services.GetRequiredService<INarniaSettingsRepository>()
            .SetAsync("shell_path", "pwsh.exe", Ct);
        var repo = factory.WindowsRepository;
        await repo.UpsertOpenAsync(100, "k1", [Tab("11111111-1111-4111-8111-111111111111", 0)], Now, Ct);
        await repo.UpsertOpenAsync(200, "k2", [Tab("22222222-2222-4222-8222-222222222222", 0)], Now, Ct);
        var ids = (await repo.GetOpenAsync(Ct)).Select(w => w.Id).ToArray();
        foreach (var id in ids)
            await repo.CloseAsync(id, Now.AddMinutes(1), Ct);

        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/windows/reopen", new { ids, separateWindows = true }, Ct);

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<BulkReopenResponse>(Ct);
        Assert.Equal(2, body!.Reopened);
        // No Windows Terminal in tests → each selected session launches via the direct fallback.
        factory.ProcessLauncher.Verify(
            p => p.Start(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task BulkReopen_EmptySelection_Returns400()
    {
        using var factory = new NarniaWebAppFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/windows/reopen", new { ids = Array.Empty<string>() }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        factory.ProcessLauncher.Verify(
            p => p.Start(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()),
            Times.Never);
    }

    [Fact]
    public async Task Reopen_KeepsClosedRecord_SoItStaysInRecentlyClosed()
    {
        // Reopening a closed window must not consume its record: the user expects a group of
        // sessions they always restore to remain available in "Recently closed" afterwards.
        using var factory = new NarniaWebAppFactory();
        await factory.Services.GetRequiredService<INarniaSettingsRepository>()
            .SetAsync("shell_path", "pwsh.exe", Ct);
        var repo = factory.WindowsRepository;
        await repo.UpsertOpenAsync(100, "k", [Tab("11111111-1111-4111-8111-111111111111", 0)], Now, Ct);
        var id = (await repo.GetOpenAsync(Ct)).Single().Id;
        await repo.CloseAsync(id, Now.AddMinutes(1), Ct);

        var client = factory.CreateClient();
        var response = await client.PostAsync($"/api/windows/{id}/reopen", null, Ct);

        response.EnsureSuccessStatusCode();
        var stillClosed = await repo.GetByIdAsync(id, Ct);
        Assert.NotNull(stillClosed);
        Assert.Equal(TerminalWindowStatus.Closed, stillClosed!.Status);
        Assert.Contains(await repo.GetClosedAsync(50, Ct), w => w.Id == id);
    }

    private sealed record BulkReopenResponse(int Reopened);

    private sealed record WindowsResponse(List<WindowDto> Open, List<WindowDto> Closed);

    private sealed record WindowDto(string Id, string Status, List<TabDto> Tabs);

    private sealed record TabDto(string SessionId, string? Summary, string? Repository, bool IsFavorite);

    private sealed record AutostartResponse(bool Supported, bool Enabled);
}
