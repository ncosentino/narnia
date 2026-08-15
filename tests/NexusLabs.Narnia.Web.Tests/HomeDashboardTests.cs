using NexusLabs.Narnia.Core.Models;
using NexusLabs.Narnia.Core.Services;

namespace NexusLabs.Narnia.Web.Tests;

public sealed class HomeDashboardTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 14, 12, 0, 0, TimeSpan.Zero);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Home_RendersOperationalDashboardWithoutRecentSessionsTable()
    {
        using var factory = new NarniaWebAppFactory();
        var session = new SessionSummary(
            "sess-1",
            @"C:\dev\example",
            "owner/example",
            "main",
            "Ship the dashboard",
            Now.AddDays(-2),
            Now.AddHours(-1),
            12,
            2)
        {
            IsFavorite = true,
        };
        factory.SessionRepository
            .Setup(repository => repository.ListAllAsync(false, It.IsAny<CancellationToken>()))
            .ReturnsAsync([session]);
        factory.SessionRepository
            .Setup(repository => repository.GetResumeSuggestionsAsync(-1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new ResumeSuggestion(
                    session with { Id = "archived-session" },
                    "Archived checkpoint",
                    "This suggestion is not visible."),
                new ResumeSuggestion(session, "Dashboard checkpoint", "Wire the recovery panel."),
            ]);

        var windows = factory.WindowsRepository;
        await windows.UpsertOpenAsync(
            100,
            "closed-window",
            [new TerminalWindowTab("sess-1", 0, @"C:\dev\example")],
            Now,
            Ct);
        var closedWindow = Assert.Single(await windows.GetOpenAsync(Ct));
        await windows.CloseAsync(closedWindow.Id, Now.AddMinutes(1), Ct);

        await factory.ScheduledJobRegistry.CreateAsync(
            new ScheduledJobDraft(
                "Nightly report",
                null,
                @"C:\dev\example",
                "Daily 05:00",
                null,
                @"C:\run.ps1",
                @"C:\logs",
                "--allow-all-tools",
                @"\Narnia\",
                "Narnia - Nightly report",
                null,
                []),
            Now,
            Ct);
        var client = factory.CreateClient();
        var html = await client.GetStringAsync("/", Ct);

        Assert.Contains("class=\"dashboard-heading-title\"", html, StringComparison.Ordinal);
        Assert.Contains("src=\"/narnia-logo.png\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("🗡️", html, StringComparison.Ordinal);
        Assert.Contains("action=\"/sessions\"", html, StringComparison.Ordinal);
        Assert.Contains("name=\"q\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/runtime/windows\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/favorites\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/schedules\"", html, StringComparison.Ordinal);
        Assert.Contains("Wire the recovery panel.", html, StringComparison.Ordinal);
        Assert.DoesNotContain("This suggestion is not visible.", html, StringComparison.Ordinal);
        Assert.Contains("narniaReopenWindow", html, StringComparison.Ordinal);
        Assert.Contains(closedWindow.Id, html, StringComparison.Ordinal);
        Assert.Contains("id=\"dashboard-schedules-content\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-resizable-table=\"home-sessions\"", html, StringComparison.Ordinal);
        factory.SessionRepository.Verify(
            repository => repository.ListRecentAsync(
                It.IsAny<int>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        factory.ScheduledTaskProvider.Verify(
            provider => provider.ListInFolderAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
