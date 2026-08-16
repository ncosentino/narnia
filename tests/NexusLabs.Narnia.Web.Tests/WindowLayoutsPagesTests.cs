using NexusLabs.Narnia.Core.Models;

namespace NexusLabs.Narnia.Web.Tests;

public sealed class WindowLayoutsPagesTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);
    private const string SessionId = "11111111-1111-4111-8111-111111111111";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task LayoutsPage_ShowsPersistedCollectionWindow()
    {
        using var factory = new NarniaWebAppFactory();
        var collection = await factory.WorkCollectionsRepository.CreateAsync(
            "Foundation",
            [SessionId],
            Now,
            Ct);
        var layout = await factory.WindowLayoutsRepository.CreateAsync(
            "Daily workspace",
            Monitors(),
            [Slot(collection.Id)],
            Now,
            Ct);

        var html = await factory.CreateClient().GetStringAsync("/layouts", Ct);

        Assert.Contains("Daily workspace", html, StringComparison.Ordinal);
        Assert.Contains("Foundation", html, StringComparison.Ordinal);
        Assert.Contains(
            $"narniaLaunchWindowLayout(&#x27;{layout.Id}&#x27;, this, false)",
            html,
            StringComparison.Ordinal);
        Assert.Contains("href=\"/layouts/capture\"", html, StringComparison.Ordinal);
        Assert.Contains("narniaCreateBlankLayout", html, StringComparison.Ordinal);
        Assert.Contains("class=\"layout-preview-stage\"", html, StringComparison.Ordinal);
        Assert.Contains("class=\"layout-preview-window layout-preview-window--0\"", html);
        Assert.Contains("left: 0%;", html, StringComparison.Ordinal);
        Assert.Matches(
            """width: 33\.\d+%;""",
            html);
        Assert.Contains("<summary>Window details</summary>", html, StringComparison.Ordinal);
        Assert.Contains($"""href="/layouts/{layout.Id}/edit">""", html);
    }

    [Fact]
    public async Task CapturePage_RendersWindowAndSuggestedCollection()
    {
        using var factory = new NarniaWebAppFactory();
        var collection = await factory.WorkCollectionsRepository.CreateAsync(
            "Foundation",
            [SessionId],
            Now,
            Ct);
        factory.SessionRepository
            .Setup(repository => repository.GetByIdsAsync(
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, Session>(StringComparer.Ordinal)
            {
                [SessionId] = new Session(
                    SessionId,
                    Path.GetTempPath(),
                    null,
                    null,
                    "Foundry",
                    null,
                    Now,
                    Now),
            });
        factory.WindowLayoutPlatform
            .Setup(platform => platform.Capture())
            .Returns(new WindowLayoutCaptureSnapshot(
                true,
                null,
                [
                    new CapturedTerminalWindow(
                        100,
                        1234,
                        "Foundry",
                        0,
                        new WindowRectangle(0, 0, 1276, 1056),
                        WindowLayoutState.Normal,
                        new WindowLayoutMonitor(
                            @"\\.\DISPLAY1",
                            true,
                            new WindowRectangle(0, 0, 3840, 2160),
                            new WindowRectangle(0, 0, 3840, 2112))),
                ],
                []));

        var html = await factory.CreateClient().GetStringAsync("/layouts/capture", Ct);

        Assert.Contains("Foundry", html, StringComparison.Ordinal);
        Assert.Contains($"value=\"{collection.Id}\" selected", html, StringComparison.Ordinal);
        Assert.Contains("data-width=\"1276\"", html, StringComparison.Ordinal);
        Assert.Contains("narniaSaveCapturedLayout", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EditorPage_RendersCanvasPalettesAndExistingWindow()
    {
        using var factory = new NarniaWebAppFactory();
        var collection = await factory.WorkCollectionsRepository.CreateAsync(
            "Foundation",
            [SessionId],
            Now,
            Ct);
        var layout = await factory.WindowLayoutsRepository.CreateAsync(
            "Editable",
            Monitors(),
            [Slot(collection.Id)],
            Now,
            Ct);
        factory.SessionRepository
            .Setup(repository => repository.ListRecentAsync(
                30,
                false,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new SessionSummary(
                    SessionId,
                    Path.GetTempPath(),
                    "owner/repository",
                    "main",
                    "Individual session",
                    Now,
                    Now,
                    1,
                    0),
            ]);
        factory.RecordedSessionRepository
            .Setup(repository => repository.GetByIdsAsync(
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, Session>(StringComparer.Ordinal)
            {
                [SessionId] = new Session(
                    SessionId,
                    Path.GetTempPath(),
                    "owner/repository",
                    "main",
                    "Recorded individual",
                    null,
                    Now,
                    Now),
            });

        var html = await factory.CreateClient()
            .GetStringAsync($"/layouts/{layout.Id}/edit", Ct);

        Assert.Contains("id=\"layout-editor\"", html, StringComparison.Ordinal);
        Assert.Contains("class=\"layout-editor-stage\"", html, StringComparison.Ordinal);
        Assert.Contains("data-layout-kind=\"collection\"", html, StringComparison.Ordinal);
        Assert.Contains($"data-layout-id=\"{collection.Id}\"", html, StringComparison.Ordinal);
        Assert.Contains("Individual sessions", html, StringComparison.Ordinal);
        Assert.Contains("Recorded: Recorded individual", html, StringComparison.Ordinal);
        Assert.Contains("narniaSaveLayoutEditor", html, StringComparison.Ordinal);
    }

    private static WindowLayoutSlotDefinition Slot(string collectionId) =>
        new(
            0,
            WindowLayoutContentKind.Collection,
            collectionId,
            null,
            "Foundry",
            @"\\.\DISPLAY1",
            true,
            new WindowRectangle(0, 0, 3840, 2112),
            new WindowRectangle(0, 0, 1276, 1056),
            new NormalizedWindowRectangle(0, 0, 1d / 3d, 0.5),
            WindowLayoutState.Normal,
            0,
            WindowLayoutDesktopPolicy.Current);

    private static IReadOnlyList<WindowLayoutMonitorDefinition> Monitors() =>
    [
        new(
            0,
            @"\\.\DISPLAY1",
            true,
            new WindowRectangle(0, 0, 3840, 2160),
            new WindowRectangle(0, 0, 3840, 2112)),
    ];
}
