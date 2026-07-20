using System.Text.Json;
using NexusLabs.Narnia.Core.Models;
using NexusLabs.Narnia.Core.Services;
using NexusLabs.Narnia.Web.Mcp;

namespace NexusLabs.Narnia.Web.Tests;

public sealed class StorageToolsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task GetSessionStorageOverviewAsync_ReturnsLargestCachedSessions()
    {
        var storage = new Mock<ISessionStorageService>();
        storage
            .Setup(service => service.GetDashboardAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Dashboard());
        var tools = CreateTools(storage: storage);

        var json = await tools.GetSessionStorageOverviewAsync(Ct);
        using var document = JsonDocument.Parse(json);

        Assert.Equal(1024, document.RootElement.GetProperty("overview")
            .GetProperty("categories").GetProperty("totalBytes").GetInt64());
        Assert.Equal(
            "session-1",
            document.RootElement.GetProperty("largestSessions")[0]
                .GetProperty("sessionId").GetString());
    }

    [Fact]
    public async Task DeleteLocalSessionsAsync_RequiresExplicitConfirmation()
    {
        var cleanup = new Mock<ISessionCleanupService>();
        var tools = CreateTools(cleanup: cleanup);

        var json = await tools.DeleteLocalSessionsAsync(
            ["session-1"],
            false,
            true,
            false,
            Ct);

        Assert.Contains("not explicitly confirmed", json, StringComparison.Ordinal);
        cleanup.Verify(service => service.DeleteAsync(
            It.IsAny<IReadOnlyCollection<string>>(),
            It.IsAny<bool>(),
            It.IsAny<bool>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ScanSessionStorageAsync_ReturnsQueueStatus()
    {
        var coordinator = new Mock<ISessionStorageScanCoordinator>();
        coordinator.Setup(service => service.RequestScan()).Returns(true);
        coordinator.Setup(service => service.GetProgress())
            .Returns(new SessionStorageScanProgress("idle", null, null, 0, 0, null));
        var tools = CreateTools(coordinator: coordinator);

        var json = await tools.ScanSessionStorageAsync(Ct);
        using var document = JsonDocument.Parse(json);

        Assert.True(document.RootElement.GetProperty("accepted").GetBoolean());
    }

    private static StorageTools CreateTools(
        Mock<ISessionStorageService>? storage = null,
        Mock<ISessionCleanupService>? cleanup = null,
        Mock<ISessionStorageScanCoordinator>? coordinator = null) =>
        new(
            (storage ?? new Mock<ISessionStorageService>()).Object,
            (cleanup ?? new Mock<ISessionCleanupService>()).Object,
            (coordinator ?? new Mock<ISessionStorageScanCoordinator>()).Object);

    private static SessionStorageDashboard Dashboard()
    {
        var record = new SessionStorageRecord
        {
            SessionId = "session-1",
            ScannedAt = DateTimeOffset.UtcNow,
            TotalBytes = 1024,
            FileCount = 1,
            EventsBytes = 1024,
            SessionDatabaseBytes = 0,
            CheckpointsBytes = 0,
            RewindBytes = 0,
            ArtifactsBytes = 0,
            OtherBytes = 0,
            LargestFileBytes = 1024,
            LargestFilePath = "events.jsonl",
            IsComplete = true,
            IsUserNamed = false,
            ContainsGitRepository = false,
            ContainsLinkedWorktree = false,
            ContainsReparsePoint = false,
        };
        return new SessionStorageDashboard(
            new SessionStorageOverview(
                new SessionStorageCategoryTotals(1024, 0, 0, 0, 0, 0),
                1,
                0,
                0,
                0,
                0,
                0),
            [
                new SessionStorageItem
                {
                    SessionId = "session-1",
                    Summary = "Session",
                    DataState = SessionStorageDataState.IndexedWithLocalState,
                    Storage = record,
                    IsActive = false,
                    IsFavorite = false,
                    IsArchived = false,
                    HasNarniaMetadata = false,
                    IsInSessionGroup = false,
                    IsInCollection = false,
                    ProtectionReasons = [],
                },
            ],
            [],
            [],
            null);
    }
}
