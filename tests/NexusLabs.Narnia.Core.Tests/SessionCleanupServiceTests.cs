using System.IO.Abstractions.TestingHelpers;
using Microsoft.Data.Sqlite;
using NexusLabs.Narnia.Core.Configuration;
using NexusLabs.Narnia.Core.Models;
using NexusLabs.Narnia.Core.Repositories;
using NexusLabs.Narnia.Core.Services;

namespace NexusLabs.Narnia.Core.Tests;

public sealed class SessionCleanupServiceTests
{
    private const string SessionId = "11111111-1111-4111-8111-111111111111";
    private const string SessionStatePath = @"C:\copilot\session-state";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task PreviewAsync_ActiveSession_IsHardBlocked()
    {
        var context = CreateContext(Item(active: true));

        var preview = await context.Service.PreviewAsync([SessionId], false, Ct);

        var decision = Assert.Single(preview.Decisions);
        Assert.Equal(SessionCleanupDisposition.Blocked, decision.Disposition);
        Assert.Contains("live Copilot", Assert.Single(decision.Reasons));
        context.CopilotManager.Verify(manager => manager.DeleteSessionsAsync(
            It.IsAny<IReadOnlyCollection<string>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PreviewAsync_ProtectedSession_RequiresExplicitOverride()
    {
        var context = CreateContext(Item(protectedSession: true));

        var protectedPreview = await context.Service.PreviewAsync([SessionId], false, Ct);
        var allowedPreview = await context.Service.PreviewAsync([SessionId], true, Ct);

        Assert.Equal(
            SessionCleanupDisposition.Protected,
            Assert.Single(protectedPreview.Decisions).Disposition);
        Assert.Equal(
            SessionCleanupDisposition.Allowed,
            Assert.Single(allowedPreview.Decisions).Disposition);
    }

    [Fact]
    public async Task PreviewAsync_UserNamedSession_IsProtected()
    {
        var context = CreateContext(Item());
        context.WorkspaceReader
            .Setup(reader => reader.ReadMetadata(SessionId))
            .Returns(new WorkspaceInfo(SessionId, null, []) { IsUserNamed = true });

        var preview = await context.Service.PreviewAsync([SessionId], false, Ct);

        var decision = Assert.Single(preview.Decisions);
        Assert.Equal(SessionCleanupDisposition.Protected, decision.Disposition);
        Assert.Contains("Named by you in Copilot", decision.Reasons);
    }

    [Fact]
    public async Task PreviewAsync_UnsafeGitArtifacts_AreHardBlocked()
    {
        var context = CreateContext(Item());
        context.GitInspector
            .Setup(inspector => inspector.InspectAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GitArtifactInspection(false, ["Unpushed commits"]));

        var preview = await context.Service.PreviewAsync([SessionId], true, Ct);

        var decision = Assert.Single(preview.Decisions);
        Assert.Equal(SessionCleanupDisposition.Blocked, decision.Disposition);
        Assert.Equal("Unpushed commits", Assert.Single(decision.Reasons));
    }

    [Fact]
    public async Task DeleteAsync_RevalidatesDeletesAuditsAndRemovesCache()
    {
        var context = CreateContext(Item());
        context.CopilotManager
            .Setup(manager => manager.DeleteSessionsAsync(
                It.Is<IReadOnlyCollection<string>>(ids => ids.SequenceEqual(new[] { SessionId })),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([new CopilotSessionDeletionResult(SessionId, true, null)]);

        var result = await context.Service.DeleteAsync([SessionId], false, true, Ct);

        var deleted = Assert.Single(result.Results);
        Assert.True(deleted.Deleted);
        Assert.True(deleted.Archived);
        Assert.Equal(1024, result.DeletedBytes);
        Assert.Equal(1, result.ArchivedCount);
        context.OverridesRepository.Verify(repository => repository.SetArchivedAsync(
            SessionId,
            true,
            It.IsAny<DateTimeOffset>(),
            It.IsAny<CancellationToken>()));
        context.StorageRepository.Verify(repository => repository.RecordCleanupAsync(
            It.Is<IReadOnlyCollection<SessionCleanupAuditEntry>>(entries =>
                entries.Count == 1 && entries.Single().Result == "deleted_archived"),
            It.IsAny<CancellationToken>()));
        context.StorageRepository.Verify(repository => repository.RemoveCurrentAsync(
            It.Is<IReadOnlyCollection<string>>(ids => ids.SequenceEqual(new[] { SessionId })),
            It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task DeleteAsync_ArchiveDisabled_LeavesArchiveFlagUnchanged()
    {
        var context = CreateContext(Item());
        context.CopilotManager
            .Setup(manager => manager.DeleteSessionsAsync(
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([new CopilotSessionDeletionResult(SessionId, true, null)]);

        var result = await context.Service.DeleteAsync([SessionId], false, false, Ct);

        var deleted = Assert.Single(result.Results);
        Assert.True(deleted.Deleted);
        Assert.False(deleted.Archived);
        context.OverridesRepository.Verify(repository => repository.SetArchivedAsync(
            It.IsAny<string>(),
            It.IsAny<bool>(),
            It.IsAny<DateTimeOffset>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteAsync_ArchiveFailure_PreservesSuccessfulDeletionWithWarning()
    {
        var context = CreateContext(Item());
        context.CopilotManager
            .Setup(manager => manager.DeleteSessionsAsync(
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([new CopilotSessionDeletionResult(SessionId, true, null)]);
        context.OverridesRepository
            .Setup(repository => repository.SetArchivedAsync(
                SessionId,
                true,
                It.IsAny<DateTimeOffset>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new SqliteException("settings database is locked", 5));

        var result = await context.Service.DeleteAsync([SessionId], false, true, Ct);

        var deleted = Assert.Single(result.Results);
        Assert.True(deleted.Deleted);
        Assert.False(deleted.Archived);
        Assert.Contains("could not archive", deleted.Error, StringComparison.Ordinal);
        context.StorageRepository.Verify(repository => repository.RecordCleanupAsync(
            It.Is<IReadOnlyCollection<SessionCleanupAuditEntry>>(entries =>
                entries.Single().Result == "deleted_archive_failed"),
            It.IsAny<CancellationToken>()));
    }

    private static CleanupTestContext CreateContext(SessionStorageItem item)
    {
        var storageService = new Mock<ISessionStorageService>();
        storageService
            .Setup(service => service.GetDashboardAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SessionStorageDashboard(
                new SessionStorageOverview(
                    new SessionStorageCategoryTotals(1024, 0, 0, 0, 0, 0),
                    1,
                    0,
                    0,
                    item.IsActive ? 1 : 0,
                    item.IsProtected ? 1 : 0,
                    0),
                [item],
                [],
                [],
                null));
        var storageRepository = new Mock<ISessionStorageRepository>();
        var overridesRepository = new Mock<ISessionOverridesRepository>();
        storageRepository
            .Setup(repository => repository.RecordCleanupAsync(
                It.IsAny<IReadOnlyCollection<SessionCleanupAuditEntry>>(),
                It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);
        storageRepository
            .Setup(repository => repository.RemoveCurrentAsync(
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);
        var workspaceReader = new Mock<IWorkspaceReader>();
        workspaceReader
            .Setup(reader => reader.ReadMetadata(SessionId))
            .Returns(new WorkspaceInfo(SessionId, null, []));
        var gitInspector = new Mock<IGitArtifactInspector>();
        gitInspector
            .Setup(inspector => inspector.InspectAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GitArtifactInspection(true, []));
        var copilotManager = new Mock<ICopilotSessionManager>();
        var fileSystem = new MockFileSystem();
        fileSystem.AddDirectory($@"{SessionStatePath}\{SessionId}");
        var now = new DateTimeOffset(2026, 7, 19, 12, 0, 0, TimeSpan.Zero);
        var service = new SessionCleanupService(
            storageService.Object,
            storageRepository.Object,
            overridesRepository.Object,
            workspaceReader.Object,
            gitInspector.Object,
            copilotManager.Object,
            new NarniaOptions
            {
                SessionStatePath = SessionStatePath,
                DatabasePath = @"C:\copilot\session-store.db",
            },
            fileSystem,
            new FixedTimeProvider(now));
        return new CleanupTestContext(
            service,
            storageRepository,
            overridesRepository,
            workspaceReader,
            gitInspector,
            copilotManager);
    }

    private static SessionStorageItem Item(
        bool active = false,
        bool protectedSession = false) =>
        new()
        {
            SessionId = SessionId,
            Summary = "Session",
            Repository = "owner/repo",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-100),
            UpdatedAt = DateTimeOffset.UtcNow.AddDays(-90),
            DataState = SessionStorageDataState.IndexedWithLocalState,
            Storage = new SessionStorageRecord
            {
                SessionId = SessionId,
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
            },
            IsActive = active,
            IsFavorite = protectedSession,
            IsArchived = false,
            HasNarniaMetadata = false,
            IsInSessionGroup = false,
            IsInCollection = false,
            ProtectionReasons = protectedSession ? ["Favorite"] : [],
        };

    private sealed record CleanupTestContext(
        SessionCleanupService Service,
        Mock<ISessionStorageRepository> StorageRepository,
        Mock<ISessionOverridesRepository> OverridesRepository,
        Mock<IWorkspaceReader> WorkspaceReader,
        Mock<IGitArtifactInspector> GitInspector,
        Mock<ICopilotSessionManager> CopilotManager);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
