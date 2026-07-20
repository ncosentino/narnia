using NexusLabs.Narnia.Core.Models;
using NexusLabs.Narnia.Core.Services;

namespace NexusLabs.Narnia.Web;

internal static class StorageEndpoints
{
    public static void MapStorageEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/storage/status", GetStatus);
        endpoints.MapPost("/api/storage/scan", RequestScan);
        endpoints.MapPost("/api/storage/cleanup-preview", PreviewCleanupAsync);
        endpoints.MapPost("/api/storage/delete", DeleteSessionsAsync);
    }

    private static IResult GetStatus(ISessionStorageScanCoordinator coordinator) =>
        Results.Ok(coordinator.GetProgress());

    private static IResult RequestScan(ISessionStorageScanCoordinator coordinator) =>
        coordinator.RequestScan()
            ? Results.Accepted("/api/storage/status", coordinator.GetProgress())
            : Results.Conflict(coordinator.GetProgress());

    private static async Task<IResult> PreviewCleanupAsync(
        StorageCleanupPreviewRequest request,
        ISessionCleanupService cleanupService,
        CancellationToken ct)
    {
        if (request.SessionIds is not { Length: > 0 })
            return Results.BadRequest("Select at least one session.");

        var preview = await cleanupService.PreviewAsync(
            request.SessionIds,
            request.OverrideProtections,
            ct);
        return Results.Ok(ToPreviewResponse(preview));
    }

    private static async Task<IResult> DeleteSessionsAsync(
        StorageCleanupDeleteRequest request,
        ISessionCleanupService cleanupService,
        CancellationToken ct)
    {
        if (request.SessionIds is not { Length: > 0 })
            return Results.BadRequest("Select at least one session.");
        if (!request.ConfirmLocalDeletion)
            return Results.BadRequest("Local session deletion must be explicitly confirmed.");
        if (request.ArchiveDeletedSessions is null)
            return Results.BadRequest("Choose whether successfully deleted sessions should be archived in Narnia.");

        var result = await cleanupService.DeleteAsync(
            request.SessionIds,
            request.OverrideProtections,
            request.ArchiveDeletedSessions.Value,
            ct);
        return Results.Ok(new
        {
            deletedCount = result.DeletedCount,
            deletedBytes = result.DeletedBytes,
            archivedCount = result.ArchivedCount,
            results = result.Results.Select(item => new
            {
                sessionId = item.SessionId,
                deleted = item.Deleted,
                archived = item.Archived,
                estimatedBytes = item.EstimatedBytes,
                reasons = item.Reasons,
                error = item.Error,
            }),
        });
    }

    private static object ToPreviewResponse(SessionCleanupPreview preview) =>
        new
        {
            allowedCount = preview.AllowedCount,
            allowedBytes = preview.AllowedBytes,
            protectedCount = preview.ProtectedCount,
            protectedBytes = preview.ProtectedBytes,
            blockedCount = preview.BlockedCount,
            decisions = preview.Decisions.Select(decision => new
            {
                sessionId = decision.SessionId,
                summary = decision.Summary,
                estimatedBytes = decision.EstimatedBytes,
                disposition = decision.Disposition.ToString().ToLowerInvariant(),
                reasons = decision.Reasons,
            }),
        };

    internal sealed record StorageCleanupPreviewRequest(
        string[] SessionIds,
        bool OverrideProtections);

    internal sealed record StorageCleanupDeleteRequest(
        string[] SessionIds,
        bool OverrideProtections,
        bool ConfirmLocalDeletion,
        bool? ArchiveDeletedSessions);
}
