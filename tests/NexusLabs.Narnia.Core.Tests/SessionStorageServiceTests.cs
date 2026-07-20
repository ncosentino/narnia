using NexusLabs.Narnia.Core.Models;
using NexusLabs.Narnia.Core.Repositories;
using NexusLabs.Narnia.Core.Services;

namespace NexusLabs.Narnia.Core.Tests;

public sealed class SessionStorageServiceTests
{
    [Fact]
    public async Task GetDashboardAsync_MergesStorageIndexProtectionsAndActivity()
    {
        var storageRepository = new Mock<ISessionStorageRepository>();
        storageRepository
            .Setup(repository => repository.GetCurrentAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([Storage("indexed", 100), Storage("local-only", 50)]);
        storageRepository
            .Setup(repository => repository.GetDailyAsync(90, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        storageRepository
            .Setup(repository => repository.GetLastScanAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((SessionStorageScanInfo?)null);
        storageRepository
            .Setup(repository => repository.GetRecentCleanupAsync(
                25,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var metadataSource = new Mock<ISessionStorageMetadataSource>();
        metadataSource
            .Setup(repository => repository.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                Metadata("indexed"),
                Metadata("history-only"),
            ]);
        var overrides = new Mock<ISessionOverridesRepository>();
        overrides
            .Setup(repository => repository.GetAllOverridesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, SessionOverride>(StringComparer.OrdinalIgnoreCase)
            {
                ["indexed"] = new SessionOverride(
                    "indexed",
                    "Alias",
                    null,
                    null,
                    "Notes",
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow)
                {
                    IsArchived = true,
                    IsFavorite = true,
                },
            });
        var groups = new Mock<ISessionGroupsRepository>();
        groups
            .Setup(repository => repository.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new SessionGroup(
                    "group",
                    "Group",
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow,
                    [new SessionGroupMember("indexed", 0)]),
            ]);
        var collections = new Mock<IWorkCollectionsRepository>();
        collections
            .Setup(repository => repository.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new WorkCollection(
                    "collection",
                    "Collection",
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow,
                    [new WorkCollectionMember("indexed", DateTimeOffset.UtcNow)]),
            ]);
        var activity = new Mock<ICopilotSessionActivityReader>();
        activity.Setup(reader => reader.GetActiveSessionIds())
            .Returns(new HashSet<string>(["indexed"], StringComparer.OrdinalIgnoreCase));
        var service = new SessionStorageService(
            storageRepository.Object,
            metadataSource.Object,
            overrides.Object,
            groups.Object,
            collections.Object,
            activity.Object);

        var dashboard = await service.GetDashboardAsync(CancellationToken.None);

        Assert.Equal(3, dashboard.Sessions.Count);
        var indexed = Assert.Single(dashboard.Sessions, item => item.SessionId == "indexed");
        Assert.Equal(SessionStorageDataState.IndexedWithLocalState, indexed.DataState);
        Assert.True(indexed.IsActive);
        Assert.True(indexed.IsProtected);
        Assert.True(indexed.IsArchived);
        Assert.Equal(4, indexed.ProtectionReasons.Count);
        Assert.Equal(1, dashboard.Overview.IndexedOnlyCount);
        Assert.Equal(1, dashboard.Overview.LocalStateOnlyCount);
        Assert.Equal(150, dashboard.Overview.Categories.TotalBytes);
    }

    private static SessionStorageMetadata Metadata(string id) =>
        new(
            id,
            @"C:\repo",
            "owner/repo",
            id,
            DateTimeOffset.UtcNow.AddDays(-2),
            DateTimeOffset.UtcNow.AddDays(-1));

    private static SessionStorageRecord Storage(string id, long bytes) =>
        new()
        {
            SessionId = id,
            ScannedAt = DateTimeOffset.UtcNow,
            TotalBytes = bytes,
            FileCount = 1,
            EventsBytes = bytes,
            SessionDatabaseBytes = 0,
            CheckpointsBytes = 0,
            RewindBytes = 0,
            ArtifactsBytes = 0,
            OtherBytes = 0,
            LargestFileBytes = bytes,
            LargestFilePath = "events.jsonl",
            IsComplete = true,
            IsUserNamed = false,
            ContainsGitRepository = false,
            ContainsLinkedWorktree = false,
            ContainsReparsePoint = false,
        };
}
