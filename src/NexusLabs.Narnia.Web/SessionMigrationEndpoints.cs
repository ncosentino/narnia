using System.Text;
using NexusLabs.Narnia.Core.Models;
using NexusLabs.Narnia.Core.Services;

namespace NexusLabs.Narnia.Web;

internal static class SessionMigrationEndpoints
{
    public static void MapSessionMigrationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
            "/api/sessions/{sessionId}/migration",
            PreviewMigrationAsync);
        endpoints.MapPost(
            "/api/sessions/{sessionId}/migration",
            MigrateSessionAsync);
        endpoints.MapGet(
            "/api/session-migrations/{sessionId}/packet",
            DownloadPacketAsync);
    }

    private static async Task<IResult> PreviewMigrationAsync(
        string sessionId,
        ISessionMigrationService migrationService,
        CancellationToken ct)
    {
        if (!Guid.TryParse(sessionId, out _))
            return Results.BadRequest(new { message = "Invalid session ID format." });

        var preview = await migrationService.PreviewAsync(sessionId, ct);
        return Results.Ok(ToPreviewResponse(preview));
    }

    private static async Task<IResult> MigrateSessionAsync(
        string sessionId,
        SessionMigrationRequest request,
        ISessionMigrationService migrationService,
        CancellationToken ct)
    {
        if (!Guid.TryParse(sessionId, out _))
            return Results.BadRequest(new { message = "Invalid session ID format." });
        if (!request.ConfirmMigration)
        {
            return Results.BadRequest(new
            {
                message = "Session migration must be explicitly confirmed.",
            });
        }

        var result = await migrationService.MigrateAsync(sessionId, ct);
        if (!result.Migrated || result.Migration is null)
        {
            return Results.Conflict(new
            {
                message = result.Error ?? "Session migration did not complete.",
                migration = result.Migration is null
                    ? null
                    : ToMigrationResponse(result.Migration),
            });
        }

        return Results.Ok(new
        {
            migrated = true,
            replacementSessionId = result.Migration.ReplacementSessionId,
            migration = ToMigrationResponse(result.Migration),
        });
    }

    private static async Task<IResult> DownloadPacketAsync(
        string sessionId,
        ISessionMigrationService migrationService,
        CancellationToken ct)
    {
        if (!Guid.TryParse(sessionId, out _))
            return Results.BadRequest(new { message = "Invalid session ID format." });

        var chunk = await migrationService.ReadPacketAsync(
            sessionId,
            0,
            1_100_000,
            ct);
        if (chunk is null)
            return Results.NotFound(new { message = "Recovery packet not found." });

        return Results.File(
            Encoding.UTF8.GetBytes(chunk.Content),
            "text/markdown; charset=utf-8",
            $"narnia-session-recovery-{sessionId}.md");
    }

    private static object ToPreviewResponse(SessionMigrationPreview preview) =>
        new
        {
            sourceSessionId = preview.SourceSessionId,
            summary = preview.Summary,
            canMigrate = preview.CanMigrate,
            blockingReason = preview.BlockingReason,
            isActive = preview.IsActive,
            turnCount = preview.TurnCount,
            checkpointCount = preview.CheckpointCount,
            todoCount = preview.TodoCount,
            resumeAssessment = new
            {
                safety = preview.ResumeAssessment.Safety.ToString().ToLowerInvariant(),
                reason = preview.ResumeAssessment.Reason,
                firstEventType = preview.ResumeAssessment.FirstEventType,
                isNestedAgent = preview.ResumeAssessment.IsNestedAgent,
            },
            references = preview.References,
            existingMigration = preview.ExistingMigration is null
                ? null
                : ToMigrationResponse(preview.ExistingMigration),
        };

    private static object ToMigrationResponse(SessionMigration migration) =>
        new
        {
            id = migration.Id,
            sourceSessionId = migration.SourceSessionId,
            replacementSessionId = migration.ReplacementSessionId,
            inPlace = migration.IsInPlace,
            status = FormatStatus(migration.Status),
            recoveryPacketBytes = migration.RecoveryPacketBytes,
            recoveryPacketTruncated = migration.RecoveryPacketTruncated,
            archivedEventsFileName = migration.ArchivedEventsPath is null
                ? null
                : Path.GetFileName(migration.ArchivedEventsPath),
            archivedEventsSha256 = migration.ArchivedEventsSha256,
            error = migration.Error,
            createdAt = migration.CreatedAt,
            updatedAt = migration.UpdatedAt,
            completedAt = migration.CompletedAt,
        };

    private static string FormatStatus(SessionMigrationStatus status) =>
        status switch
        {
            SessionMigrationStatus.SessionCreated => "session_created",
            SessionMigrationStatus.CleanupRequired => "cleanup_required",
            _ => status.ToString().ToLowerInvariant(),
        };

    internal sealed record SessionMigrationRequest(bool ConfirmMigration);
}
