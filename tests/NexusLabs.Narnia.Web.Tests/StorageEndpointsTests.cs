using System.Net;
using System.Net.Http.Json;
using NexusLabs.Narnia.Core.Models;

namespace NexusLabs.Narnia.Web.Tests;

public sealed class StorageEndpointsTests
{
    private const string SessionId = "11111111-1111-4111-8111-111111111111";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task StoragePage_RendersGuidedScopeReviewAndCleanupFlow()
    {
        using var factory = new NarniaWebAppFactory();
        var now = new DateTimeOffset(2026, 7, 19, 12, 0, 0, TimeSpan.Zero);
        await factory.StorageRepository.SaveScanAsync(
            [Storage(now)],
            now,
            now.AddMinutes(1),
            Ct);
        factory.StorageMetadataSource
            .Setup(repository => repository.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([Metadata(now)]);

        var html = await factory.CreateClient()
            .GetStringAsync("/storage?view=candidates&minMb=0&showProtected=true", Ct);

        Assert.Contains("<h1>Session Storage</h1>", html, StringComparison.Ordinal);
        Assert.Contains("1 KiB", html, StringComparison.Ordinal);
        Assert.Contains("Storage session", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/files\"", html, StringComparison.Ordinal);
        Assert.Contains("class=\"storage-check\"", html, StringComparison.Ordinal);
        Assert.Contains("What is a protected session?", html, StringComparison.Ordinal);
        Assert.Contains("Show protected sessions in candidate results", html, StringComparison.Ordinal);
        Assert.Contains("Protected candidates", html, StringComparison.Ordinal);
        Assert.Contains("Meet the current thresholds but are hidden", html, StringComparison.Ordinal);
        Assert.Contains("class=\"storage-row--protected", html, StringComparison.Ordinal);
        Assert.Contains("Named by you in Copilot", html, StringComparison.Ordinal);
        Assert.Contains("Session activity", html, StringComparison.Ordinal);
        Assert.Contains("Latest file write", html, StringComparison.Ordinal);
        Assert.Contains("Review cleanup plan", html, StringComparison.Ordinal);
        Assert.Contains("id=\"storage-cleanup-dialog\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Explicitly include protected selections", html, StringComparison.Ordinal);
        Assert.True(
            html.IndexOf("Define the sessions you want to review", StringComparison.Ordinal) <
            html.IndexOf("Understand this scope", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StoragePage_SearchUpdatesScopeBeforeSummaryAndCharts()
    {
        using var factory = new NarniaWebAppFactory();
        var now = new DateTimeOffset(2026, 7, 19, 12, 0, 0, TimeSpan.Zero);
        await factory.StorageRepository.SaveScanAsync(
            [Storage(now)],
            now,
            now.AddMinutes(1),
            Ct);
        factory.StorageMetadataSource
            .Setup(repository => repository.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([Metadata(now)]);

        var html = await factory.CreateClient()
            .GetStringAsync("/storage?view=all&q=does-not-match", Ct);

        Assert.Contains("0 sessions match this analysis", html, StringComparison.Ordinal);
        Assert.Contains("No local storage matches this scope.", html, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"storage-category-chart\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StorageJavaScript_UsesReviewDialogInsteadOfAmbiguousOverride()
    {
        var javascript = await File.ReadAllTextAsync(FindStorageJavaScript(), Ct);

        Assert.Contains("narniaRenderStorageDecisionList", javascript, StringComparison.Ordinal);
        Assert.Contains("narniaStoragePlanChanged", javascript, StringComparison.Ordinal);
        Assert.Contains("narniaStorageCleanupCompleted", javascript, StringComparison.Ordinal);
        Assert.Contains("storage-plan-include-protected", javascript, StringComparison.Ordinal);
        Assert.DoesNotContain("storage-override-protections", javascript, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RequestScan_AcceptedWhenCoordinatorQueuesRequest()
    {
        using var factory = new NarniaWebAppFactory();

        var response = await factory.CreateClient().PostAsync("/api/storage/scan", null, Ct);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        factory.StorageScanCoordinator.Verify(coordinator => coordinator.RequestScan());
    }

    [Fact]
    public async Task CleanupPreview_ReturnsDispositionAndBytes()
    {
        using var factory = new NarniaWebAppFactory();
        factory.SessionCleanupService
            .Setup(service => service.PreviewAsync(
                It.IsAny<IReadOnlyCollection<string>>(),
                false,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SessionCleanupPreview(
                [new SessionCleanupDecision(
                    SessionId,
                    "Session",
                    1024,
                    SessionCleanupDisposition.Allowed,
                    [])],
                1,
                1024,
                0,
                0,
                0));

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/storage/cleanup-preview",
            new
            {
                sessionIds = new[] { SessionId },
                overrideProtections = false,
                confirmLocalDeletion = false,
            },
            Ct);
        var body = await response.Content.ReadFromJsonAsync<PreviewResponse>(Ct);

        response.EnsureSuccessStatusCode();
        Assert.NotNull(body);
        Assert.Equal(1, body!.AllowedCount);
        Assert.Equal("allowed", Assert.Single(body.Decisions).Disposition);
    }

    [Fact]
    public async Task Delete_RequiresExplicitConfirmation()
    {
        using var factory = new NarniaWebAppFactory();

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/storage/delete",
            new
            {
                sessionIds = new[] { SessionId },
                overrideProtections = false,
                confirmLocalDeletion = false,
            },
            Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        factory.SessionCleanupService.Verify(service => service.DeleteAsync(
            It.IsAny<IReadOnlyCollection<string>>(),
            It.IsAny<bool>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    private static SessionStorageRecord Storage(DateTimeOffset now) =>
        new()
        {
            SessionId = SessionId,
            ScannedAt = now,
            TotalBytes = 1024,
            FileCount = 1,
            LastWriteAt = now,
            EventsBytes = 1024,
            SessionDatabaseBytes = 0,
            CheckpointsBytes = 0,
            RewindBytes = 0,
            ArtifactsBytes = 0,
            OtherBytes = 0,
            LargestFileBytes = 1024,
            LargestFilePath = "events.jsonl",
            IsComplete = true,
            IsUserNamed = true,
            ContainsGitRepository = false,
            ContainsLinkedWorktree = false,
            ContainsReparsePoint = false,
        };

    private static SessionStorageMetadata Metadata(DateTimeOffset now) =>
        new(
            SessionId,
            @"C:\repo",
            "owner/repo",
            "Storage session",
            now.AddDays(-100),
            now.AddDays(-90));

    private sealed record PreviewResponse(
        int AllowedCount,
        long AllowedBytes,
        List<PreviewDecision> Decisions);

    private sealed record PreviewDecision(string SessionId, string Disposition);

    private static string FindStorageJavaScript()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "src",
                "NexusLabs.Narnia.Web",
                "wwwroot",
                "js",
                "narnia-charts.js");
            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException("Could not locate narnia-charts.js from the test output directory.");
    }
}
