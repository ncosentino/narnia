using System.IO.Abstractions.TestingHelpers;
using NexusLabs.Narnia.Core.Configuration;
using NexusLabs.Narnia.Core.Models;
using NexusLabs.Narnia.Core.Repositories;
using NexusLabs.Narnia.Core.Services;

namespace NexusLabs.Narnia.Core.Tests;

public sealed class SessionMigrationServiceTests
{
    private const string SourceId = "77777777-7777-4777-8777-777777777777";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task MigrateAsync_SuccessfullyCreatesAndFinalizesSuccessor()
    {
        var fixture = CreateFixture();
        SessionMigration? added = null;
        fixture.Migrations
            .Setup(repository => repository.AddAsync(
                It.IsAny<SessionMigration>(),
                It.IsAny<CancellationToken>()))
            .Callback((SessionMigration migration, CancellationToken _) => added = migration)
            .Returns(ValueTask.CompletedTask);
        fixture.Migrations
            .Setup(repository => repository.MarkSessionCreatedAsync(
                It.IsAny<string>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);
        fixture.Migrations
            .Setup(repository => repository.CompleteAsync(
                It.IsAny<string>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        fixture.Migrations
            .Setup(repository => repository.GetByIdAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => added is null
                ? null
                : added with
                {
                    Status = SessionMigrationStatus.Completed,
                    CompletedAt = DateTimeOffset.UtcNow,
                });
        var result = await fixture.Service.MigrateAsync(SourceId, Ct);

        Assert.True(result.Migrated);
        Assert.NotNull(result.Migration);
        Assert.Equal(SessionMigrationStatus.Completed, result.Migration!.Status);
        fixture.Copilot.Verify(manager => manager.CreateRecoverySessionAsync(
            It.Is<CopilotRecoverySessionRequest>(request =>
                request.SessionId == SourceId &&
                request.WorkingDirectory == @"C:\repo" &&
                request.BootstrapPrompt == "bootstrap"),
            It.IsAny<CancellationToken>()), Times.Once);
        fixture.Migrations.Verify(repository => repository.CompleteAsync(
            It.IsAny<string>(),
            It.IsAny<DateTimeOffset>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task MigrateAsync_ActiveSource_IsBlockedBeforePacketCreation()
    {
        var fixture = CreateFixture(active: true);

        var result = await fixture.Service.MigrateAsync(SourceId, Ct);

        Assert.False(result.Migrated);
        Assert.Contains("live Copilot process", result.Error, StringComparison.Ordinal);
        fixture.PacketBuilder.Verify(builder => builder.BuildAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
        fixture.Copilot.Verify(manager => manager.CreateRecoverySessionAsync(
            It.IsAny<CopilotRecoverySessionRequest>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task MigrateAsync_CopilotCreationFails_RecordsFailure()
    {
        var fixture = CreateFixture();
        fixture.Migrations
            .Setup(repository => repository.AddAsync(
                It.IsAny<SessionMigration>(),
                It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);
        fixture.Migrations
            .Setup(repository => repository.MarkFailedAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);
        fixture.Copilot
            .Setup(manager => manager.CreateRecoverySessionAsync(
                It.IsAny<CopilotRecoverySessionRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CopilotRecoverySessionResult(SourceId, false, "runtime failed"));

        var result = await fixture.Service.MigrateAsync(SourceId, Ct);

        Assert.False(result.Migrated);
        Assert.Equal("runtime failed", result.Error);
        fixture.Migrations.Verify(repository => repository.MarkFailedAsync(
            It.IsAny<string>(),
            "runtime failed",
            It.IsAny<DateTimeOffset>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PreviewAsync_ResumableSource_IsBlocked()
    {
        var fixture = CreateFixture(safety: SessionResumeSafety.Resumable);

        var preview = await fixture.Service.PreviewAsync(SourceId, Ct);

        Assert.False(preview.CanMigrate);
        Assert.Contains("passes Narnia's resume-safety check", preview.BlockingReason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PreviewAsync_RecentPreparingMigration_IsBlocked()
    {
        var fixture = CreateFixture();
        var now = new DateTimeOffset(2026, 7, 24, 12, 0, 0, TimeSpan.Zero);
        fixture.Migrations
            .Setup(repository => repository.GetLatestBySourceAsync(
                SourceId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SessionMigration(
                "migration",
                SourceId,
                "88888888-8888-4888-8888-888888888888",
                SessionMigrationStatus.Preparing,
                @"C:\narnia\recovery.md",
                100,
                false,
                null,
                now,
                now,
                null));

        var preview = await fixture.Service.PreviewAsync(SourceId, Ct);

        Assert.False(preview.CanMigrate);
        Assert.Contains("already being prepared", preview.BlockingReason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PreviewAsync_CompletedInPlaceMigration_AllowsRepeatRecovery()
    {
        var fixture = CreateFixture();
        var now = new DateTimeOffset(2026, 7, 24, 12, 0, 0, TimeSpan.Zero);
        var completed = new SessionMigration(
            "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa",
            SourceId,
            SourceId,
            SessionMigrationStatus.Completed,
            @"C:\narnia\recoveries\first\recovery.md",
            100,
            false,
            null,
            now.AddDays(-1),
            now.AddDays(-1),
            now.AddDays(-1));
        fixture.Migrations
            .Setup(repository => repository.GetLatestBySourceAsync(
                SourceId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(completed);

        var preview = await fixture.Service.PreviewAsync(SourceId, Ct);

        Assert.True(preview.CanMigrate);
        Assert.Same(completed, preview.ExistingMigration);
    }

    [Fact]
    public async Task MigrateAsync_CompletedInPlaceMigration_CreatesNewRecoveryGeneration()
    {
        var fixture = CreateFixture();
        var now = new DateTimeOffset(2026, 7, 24, 12, 0, 0, TimeSpan.Zero);
        var completed = new SessionMigration(
            "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa",
            SourceId,
            SourceId,
            SessionMigrationStatus.Completed,
            @"C:\narnia\recoveries\first\recovery.md",
            100,
            false,
            null,
            now.AddDays(-1),
            now.AddDays(-1),
            now.AddDays(-1));
        SessionMigration? added = null;
        fixture.Migrations
            .Setup(repository => repository.GetLatestBySourceAsync(
                SourceId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(completed);
        fixture.Migrations
            .Setup(repository => repository.AddAsync(
                It.IsAny<SessionMigration>(),
                It.IsAny<CancellationToken>()))
            .Callback((SessionMigration migration, CancellationToken _) => added = migration)
            .Returns(ValueTask.CompletedTask);
        fixture.Migrations
            .Setup(repository => repository.MarkSessionCreatedAsync(
                It.IsAny<string>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);
        fixture.Migrations
            .Setup(repository => repository.CompleteAsync(
                It.IsAny<string>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        fixture.Migrations
            .Setup(repository => repository.GetByIdAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => added is null
                ? null
                : added with
                {
                    Status = SessionMigrationStatus.Completed,
                    CompletedAt = now,
                });

        var result = await fixture.Service.MigrateAsync(SourceId, Ct);

        Assert.True(result.Migrated);
        Assert.NotNull(result.Migration);
        Assert.NotEqual(completed.Id, result.Migration!.Id);
        fixture.Migrations.Verify(repository => repository.AddAsync(
            It.Is<SessionMigration>(migration =>
                migration.SourceSessionId == SourceId &&
                migration.ReplacementSessionId == SourceId &&
                migration.Id != completed.Id),
            It.IsAny<CancellationToken>()), Times.Once);
        fixture.Migrations.Verify(repository => repository.RestartAsync(
            It.IsAny<SessionMigration>(),
            It.IsAny<CancellationToken>()), Times.Never);
        fixture.PacketBuilder.Verify(builder => builder.BuildAsync(
            SourceId,
            SourceId,
            result.Migration.Id,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task MigrateAsync_StalePreparingMigration_IsResetBeforeRetry()
    {
        var fixture = CreateFixture();
        var now = new DateTimeOffset(2026, 7, 24, 12, 0, 0, TimeSpan.Zero);
        var stale = new SessionMigration(
            "stale-migration",
            SourceId,
            "99999999-9999-4999-8999-999999999999",
            SessionMigrationStatus.Preparing,
            @"C:\narnia\stale.md",
            100,
            false,
            null,
            now.AddMinutes(-20),
            now.AddMinutes(-20),
            null);
        fixture.Migrations
            .Setup(repository => repository.GetLatestBySourceAsync(
                SourceId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(stale);
        fixture.Migrations
            .Setup(repository => repository.ResetAsync(
                stale.Id,
                It.IsAny<string>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        fixture.Migrations
            .Setup(repository => repository.AddAsync(
                It.IsAny<SessionMigration>(),
                It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);
        fixture.Migrations
            .Setup(repository => repository.MarkSessionCreatedAsync(
                It.IsAny<string>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);
        fixture.Migrations
            .Setup(repository => repository.CompleteAsync(
                It.IsAny<string>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        fixture.Copilot
            .Setup(manager => manager.DeleteSessionsAsync(
                It.Is<IReadOnlyCollection<string>>(ids =>
                    ids.Contains(stale.ReplacementSessionId)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new CopilotSessionDeletionResult(
                    stale.ReplacementSessionId,
                    false,
                    "Session is not available through the local Copilot SDK runtime."),
            ]);
        var result = await fixture.Service.MigrateAsync(SourceId, Ct);

        Assert.True(result.Migrated);
        fixture.Migrations.Verify(repository => repository.ResetAsync(
            stale.Id,
            It.Is<string>(error => error.Contains("Stale", StringComparison.Ordinal)),
            It.IsAny<DateTimeOffset>(),
            CancellationToken.None), Times.Once);
        fixture.Copilot.Verify(manager => manager.CreateRecoverySessionAsync(
            It.IsAny<CopilotRecoverySessionRequest>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task MigrateAsync_FailedCreationWithUndeletableSuccessor_RequiresCleanup()
    {
        var fixture = CreateFixture();
        fixture.Migrations
            .Setup(repository => repository.AddAsync(
                It.IsAny<SessionMigration>(),
                It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);
        fixture.Migrations
            .Setup(repository => repository.MarkCleanupRequiredAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);
        fixture.Copilot
            .Setup(manager => manager.CreateRecoverySessionAsync(
                It.IsAny<CopilotRecoverySessionRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CopilotRecoverySessionResult(SourceId, false, "bootstrap failed"));
        fixture.EventRecovery
            .Setup(recovery => recovery.RestoreAsync(
                SourceId,
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SessionEventRestoreResult(
                false,
                null,
                "session is locked"));

        var result = await fixture.Service.MigrateAsync(SourceId, Ct);

        Assert.False(result.Migrated);
        Assert.NotNull(result.Migration);
        Assert.Equal(SessionMigrationStatus.CleanupRequired, result.Migration!.Status);
        Assert.Contains("session is locked", result.Error, StringComparison.Ordinal);
        fixture.Migrations.Verify(repository => repository.MarkCleanupRequiredAsync(
            It.IsAny<string>(),
            It.Is<string>(error => error.Contains("session is locked", StringComparison.Ordinal)),
            It.IsAny<DateTimeOffset>(),
            CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task MigrateAsync_MissingCreatedSuccessor_DoesNotMoveReferences()
    {
        var fixture = CreateFixture();
        var now = new DateTimeOffset(2026, 7, 24, 12, 0, 0, TimeSpan.Zero);
        var existing = new SessionMigration(
            "created-migration",
            SourceId,
            SourceId,
            SessionMigrationStatus.SessionCreated,
            @"C:\narnia\recovery.md",
            100,
            false,
            null,
            now,
            now,
            null)
        {
            ArchivedEventsPath =
                $@"C:\copilot\session-state\{SourceId}\events.pre-recovery.created-migration.jsonl",
            ArchivedEventsSha256 = "ABC123",
            BaselineTurnCount = 5,
            BaselineUpdatedAt = now,
        };
        fixture.Migrations
            .Setup(repository => repository.GetLatestBySourceAsync(
                SourceId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        fixture.Migrations
            .Setup(repository => repository.MarkFailedAsync(
                existing.Id,
                It.IsAny<string>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);
        fixture.Copilot
            .Setup(manager => manager.CheckSessionAvailabilityAsync(
                existing.ReplacementSessionId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CopilotSessionAvailabilityResult(
                existing.ReplacementSessionId,
                true,
                false,
                null));

        var result = await fixture.Service.MigrateAsync(SourceId, Ct);

        Assert.False(result.Migrated);
        Assert.Contains("did not retain", result.Error, StringComparison.Ordinal);
        fixture.Migrations.Verify(repository => repository.CompleteAsync(
            It.IsAny<string>(),
            It.IsAny<DateTimeOffset>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task MigrateAsync_FailedInPlaceRecovery_ReusesMigrationRecord()
    {
        var fixture = CreateFixture();
        var now = new DateTimeOffset(2026, 7, 24, 12, 0, 0, TimeSpan.Zero);
        var failed = new SessionMigration(
            "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb",
            SourceId,
            SourceId,
            SessionMigrationStatus.Failed,
            @"C:\narnia\old-recovery.md",
            100,
            false,
            "failed",
            now.AddMinutes(-5),
            now.AddMinutes(-5),
            null);
        fixture.Migrations
            .Setup(repository => repository.GetLatestBySourceAsync(
                SourceId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(failed);
        fixture.Migrations
            .Setup(repository => repository.RestartAsync(
                It.IsAny<SessionMigration>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        fixture.Migrations
            .Setup(repository => repository.MarkSessionCreatedAsync(
                failed.Id,
                It.IsAny<DateTimeOffset>(),
                It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);
        fixture.Migrations
            .Setup(repository => repository.CompleteAsync(
                failed.Id,
                It.IsAny<DateTimeOffset>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await fixture.Service.MigrateAsync(SourceId, Ct);

        Assert.True(result.Migrated);
        Assert.Equal(failed.Id, result.Migration!.Id);
        fixture.Migrations.Verify(repository => repository.AddAsync(
            It.IsAny<SessionMigration>(),
            It.IsAny<CancellationToken>()), Times.Never);
        fixture.Migrations.Verify(repository => repository.RestartAsync(
            It.Is<SessionMigration>(migration =>
                migration.Id == failed.Id &&
                migration.SourceSessionId == SourceId &&
                migration.ReplacementSessionId == SourceId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private static Fixture CreateFixture(
        bool active = false,
        SessionResumeSafety safety = SessionResumeSafety.Incompatible)
    {
        var now = new DateTimeOffset(2026, 7, 24, 12, 0, 0, TimeSpan.Zero);
        var recovered = false;
        var sessions = new Mock<ISessionRepository>();
        sessions
            .Setup(repository => repository.GetByIdAsync(
                SourceId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new Session(
                    SourceId,
                    @"C:\repo",
                    "owner/repo",
                    "feature",
                    "Session",
                    @"C:\repo",
                    now.AddDays(-1),
                    recovered ? now.AddMinutes(1) : now,
                    recovered ? 6 : 5,
                    2));
        sessions
            .Setup(repository => repository.GetByIdAsync(
                It.Is<string>(sessionId => sessionId != SourceId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string sessionId, CancellationToken _) => new Session(
                sessionId,
                @"C:\repo",
                "owner/repo",
                "feature",
                "Recovered session",
                @"C:\repo",
                now,
                now,
                1,
                0));
        var resume = new Mock<ISessionResumeSafetyReader>();
        resume
            .Setup(reader => reader.Inspect(SourceId))
            .Returns(() => recovered
                ? new SessionResumeAssessment(
                    SourceId,
                    SessionResumeSafety.Resumable,
                    null,
                    "session.start",
                    false)
                : new SessionResumeAssessment(
                    SourceId,
                    safety,
                    safety == SessionResumeSafety.Incompatible ? "Missing session.start." : null,
                    safety == SessionResumeSafety.Resumable ? "session.start" : "system.message",
                    safety == SessionResumeSafety.Incompatible));
        var tasks = new Mock<ISessionTaskStateReader>();
        tasks
            .Setup(reader => reader.Read(SourceId))
            .Returns(new SessionTaskState(
                [new SessionTaskItem("todo", "Task", null, "done", now, now)],
                [],
                null));
        var migrations = new Mock<ISessionMigrationRepository>();
        migrations
            .Setup(repository => repository.GetLatestBySourceAsync(
                SourceId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((SessionMigration?)null);
        migrations
            .Setup(repository => repository.GetReferenceSummaryAsync(
                SourceId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SessionMigrationReferenceSummary(true, true, true, 1, 0, 1));
        var packetBuilder = new Mock<ISessionRecoveryPacketBuilder>();
        packetBuilder
            .Setup(builder => builder.BuildAsync(
                SourceId,
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SessionRecoveryPacketBuildResult(
                true,
                @"C:\narnia\recoveries\recovery.md",
                100,
                false,
                "bootstrap",
                null));
        var eventRecovery = new Mock<ISessionEventStreamRecovery>();
        eventRecovery
            .Setup(recovery => recovery.PlanAsync(
                SourceId,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, string migrationId, CancellationToken _) =>
                new SessionEventArchivePlanResult(
                    true,
                    $@"C:\copilot\session-state\{SourceId}\events.pre-recovery.{migrationId}.jsonl",
                    "ABC123",
                    null));
        eventRecovery
            .Setup(recovery => recovery.ArchiveAsync(
                SourceId,
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, string archivePath, string sha256, CancellationToken _) =>
                new SessionEventArchiveResult(
                    true,
                    archivePath,
                    sha256,
                    null));
        eventRecovery
            .Setup(recovery => recovery.RestoreAsync(
                SourceId,
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SessionEventRestoreResult(true, null, null));
        var copilot = new Mock<ICopilotSessionManager>();
        copilot
            .Setup(manager => manager.CreateRecoverySessionAsync(
                It.IsAny<CopilotRecoverySessionRequest>(),
                It.IsAny<CancellationToken>()))
            .Callback(() => recovered = true)
            .ReturnsAsync((CopilotRecoverySessionRequest request, CancellationToken _) =>
                new CopilotRecoverySessionResult(request.SessionId, true, null));
        copilot
            .Setup(manager => manager.CheckSessionAvailabilityAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string sessionId, CancellationToken _) =>
                new CopilotSessionAvailabilityResult(sessionId, true, true, null));
        copilot
            .Setup(manager => manager.DeleteSessionsAsync(
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<string> sessionIds, CancellationToken _) =>
                sessionIds.Select(sessionId => new CopilotSessionDeletionResult(
                    sessionId,
                    false,
                    "Session is not available through the local Copilot SDK runtime.")).ToArray());
        var activity = new Mock<ICopilotSessionActivityReader>();
        activity
            .Setup(reader => reader.GetActiveSessionIds())
            .Returns(active
                ? new HashSet<string>([SourceId], StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        var service = new SessionMigrationService(
            sessions.Object,
            resume.Object,
            tasks.Object,
            migrations.Object,
            packetBuilder.Object,
            eventRecovery.Object,
            copilot.Object,
            activity.Object,
            new SessionOperationCoordinator(),
            new NarniaOptions { RecoveryDirectory = @"C:\narnia\recoveries" },
            new MockFileSystem(),
            new FixedTimeProvider(now));
        return new Fixture(service, migrations, packetBuilder, eventRecovery, copilot);
    }

    private sealed record Fixture(
        SessionMigrationService Service,
        Mock<ISessionMigrationRepository> Migrations,
        Mock<ISessionRecoveryPacketBuilder> PacketBuilder,
        Mock<ISessionEventStreamRecovery> EventRecovery,
        Mock<ICopilotSessionManager> Copilot);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
