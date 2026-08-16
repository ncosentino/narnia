using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using NexusLabs.Narnia.Core.Models;
using NexusLabs.Narnia.Core.Repositories;
using NexusLabs.Narnia.Core.Services;

namespace NexusLabs.Narnia.Web.Tests;

public sealed class WindowLayoutsEndpointsTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);
    private const string SessionId = "11111111-1111-4111-8111-111111111111";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CreateLayout_PersistsCapturedPlacement()
    {
        using var factory = new NarniaWebAppFactory();
        var collection = await factory.WorkCollectionsRepository.CreateAsync(
            "Foundation",
            [SessionId],
            Now,
            Ct);

        using var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/layouts",
            new
            {
                name = "  Daily workspace  ",
                windows = new[]
                {
                    WindowRequest(collection.Id, "Foundry", 0),
                },
            },
            Ct);

        response.EnsureSuccessStatusCode();
        var layout = Assert.Single(await factory.WindowLayoutsRepository.GetAllAsync(Ct));
        Assert.Equal("Daily workspace", layout.Name);
        var slot = Assert.Single(layout.Slots);
        Assert.Equal(collection.Id, slot.CollectionId);
        Assert.Equal(new NormalizedWindowRectangle(0, 0, 1d / 3d, 0.5), slot.NormalizedBounds);
    }

    [Fact]
    public async Task CaptureLayout_SuggestsCollectionFromTerminalTitle()
    {
        using var factory = new NarniaWebAppFactory();
        var collection = await factory.WorkCollectionsRepository.CreateAsync(
            "Foundation",
            [SessionId],
            Now,
            Ct);
        GivenSession(factory, "Foundry");
        factory.WindowLayoutPlatform
            .Setup(platform => platform.Capture())
            .Returns(Snapshot([CapturedWindow("Foundry", 100)]));

        var response = await factory.CreateClient()
            .GetFromJsonAsync<CaptureResponse>("/api/layouts/capture", Ct);

        Assert.NotNull(response);
        var window = Assert.Single(response!.Windows);
        Assert.Equal(collection.Id, window.SuggestedCollectionId);
        Assert.Equal("Foundry", window.Title);
    }

    [Fact]
    public async Task LaunchLayout_LaunchesDetectsAndPositionsCollectionWindow()
    {
        using var factory = new NarniaWebAppFactory();
        var collection = await factory.WorkCollectionsRepository.CreateAsync(
            "Foundation",
            [SessionId],
            Now,
            Ct);
        var layout = await factory.WindowLayoutsRepository.CreateAsync(
            "Daily",
            [Slot(collection.Id)],
            Now,
            Ct);
        GivenSession(factory, "Foundry");
        await factory.Services.GetRequiredService<INarniaSettingsRepository>()
            .SetAsync("shell_path", "pwsh.exe", Ct);
        factory.CommandBuilder
            .Setup(builder => builder.FindWindowsTerminalPath())
            .Returns("wt.exe");
        factory.CommandBuilder
            .Setup(builder => builder.BuildNewWindowCommand(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<TerminalLaunchTab>>(),
                It.IsAny<string>()))
            .Returns("-w new new-tab");
        var launchedWindow = CapturedWindow("Foundry", 200);
        factory.WindowLayoutPlatform
            .Setup(platform => platform.Capture())
            .Returns(Snapshot([]));
        factory.WindowLayoutPlatform
            .Setup(platform => platform.WaitForNewTerminalWindowAsync(
                It.IsAny<IReadOnlySet<long>>(),
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(launchedWindow);
        factory.WindowLayoutPlatform
            .Setup(platform => platform.ApplyPlacement(
                launchedWindow.Handle,
                It.IsAny<ResolvedWindowLayoutPlacement>()))
            .Returns((long _, ResolvedWindowLayoutPlacement placement) =>
                new WindowLayoutPlacementResult(true, placement.Bounds, null));

        using var response = await factory.CreateClient().PostAsJsonAsync(
            $"/api/layouts/{layout.Id}/launch",
            new { force = false },
            Ct);

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<LaunchResponse>(Ct);
        Assert.True(body!.Success);
        var window = Assert.Single(body.Windows);
        Assert.Equal("Foundation", window.CollectionName);
        Assert.True(window.Success);
        Assert.Equal("exact", window.Adaptation);
        factory.ProcessLauncher.Verify(
            launcher => launcher.Start("wt.exe", "-w new new-tab", null),
            Times.Once);
        factory.WindowLayoutPlatform.Verify(
            platform => platform.ApplyPlacement(
                launchedWindow.Handle,
                It.Is<ResolvedWindowLayoutPlacement>(placement =>
                    placement.Bounds == new WindowRectangle(0, 0, 1276, 1056))),
            Times.Once);
        factory.WindowLayoutPlatform.Verify(
            platform => platform.Capture(),
            Times.AtLeast(2));
    }

    [Fact]
    public async Task LaunchLayout_ActiveSessionBlocksBeforeSpawning()
    {
        using var factory = new NarniaWebAppFactory();
        var collection = await factory.WorkCollectionsRepository.CreateAsync(
            "Foundation",
            [SessionId],
            Now,
            Ct);
        var layout = await factory.WindowLayoutsRepository.CreateAsync(
            "Daily",
            [Slot(collection.Id)],
            Now,
            Ct);
        GivenSession(factory, "Foundry");
        factory.CommandBuilder
            .Setup(builder => builder.FindWindowsTerminalPath())
            .Returns("wt.exe");
        factory.WindowLayoutPlatform
            .Setup(platform => platform.Capture())
            .Returns(Snapshot([]));
        factory.SessionActivityReader
            .Setup(reader => reader.GetActiveSessionIds())
            .Returns(new HashSet<string>([SessionId], StringComparer.OrdinalIgnoreCase));

        using var response = await factory.CreateClient().PostAsJsonAsync(
            $"/api/layouts/{layout.Id}/launch",
            new { force = false },
            Ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PreflightResponse>(Ct);
        Assert.Contains("already active", Assert.Single(body!.Issues), StringComparison.Ordinal);
        factory.ProcessLauncher.VerifyNoOtherCalls();
    }

    private static object WindowRequest(string collectionId, string title, int zOrder) =>
        new
        {
            collectionId,
            capturedWindowTitle = title,
            monitorDeviceName = @"\\.\DISPLAY1",
            monitorIsPrimary = true,
            capturedWorkArea = new { x = 0, y = 0, width = 3840, height = 2112 },
            capturedBounds = new { x = 0, y = 0, width = 1280, height = 1056 },
            windowState = "normal",
            zOrder,
        };

    private static WindowLayoutSlotDefinition Slot(string collectionId) =>
        new(
            0,
            collectionId,
            "Foundry",
            @"\\.\DISPLAY1",
            true,
            new WindowRectangle(0, 0, 3840, 2112),
            new WindowRectangle(0, 0, 1276, 1056),
            new NormalizedWindowRectangle(0, 0, 1d / 3d, 0.5),
            WindowLayoutState.Normal,
            0,
            WindowLayoutDesktopPolicy.Current);

    private static CapturedTerminalWindow CapturedWindow(string title, long handle) =>
        new(
            handle,
            1234,
            title,
            0,
            new WindowRectangle(0, 0, 1276, 1056),
            WindowLayoutState.Normal,
            Monitor());

    private static WindowLayoutCaptureSnapshot Snapshot(
        IReadOnlyList<CapturedTerminalWindow> windows) =>
        new(true, null, windows, [Monitor()]);

    private static WindowLayoutMonitor Monitor() =>
        new(
            @"\\.\DISPLAY1",
            true,
            new WindowRectangle(0, 0, 3840, 2160),
            new WindowRectangle(0, 0, 3840, 2112));

    private static void GivenSession(NarniaWebAppFactory factory, string summary)
    {
        var session = new Session(
            SessionId,
            Path.GetTempPath(),
            "owner/repository",
            "main",
            summary,
            null,
            Now,
            Now);
        factory.SessionRepository
            .Setup(repository => repository.GetByIdsAsync(
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, Session>(StringComparer.Ordinal)
            {
                [SessionId] = session,
            });
    }

    private sealed record CaptureResponse(List<CapturedWindowResponse> Windows);

    private sealed record CapturedWindowResponse(
        string Title,
        string? SuggestedCollectionId);

    private sealed record LaunchResponse(bool Success, List<LaunchedWindowResponse> Windows);

    private sealed record LaunchedWindowResponse(
        string CollectionName,
        bool Success,
        string? Adaptation);

    private sealed record PreflightResponse(List<string> Issues);
}
