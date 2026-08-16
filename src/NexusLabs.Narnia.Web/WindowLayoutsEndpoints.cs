using NexusLabs.Narnia.Core.Models;
using NexusLabs.Narnia.Core.Repositories;

namespace NexusLabs.Narnia.Web;

internal static class WindowLayoutsEndpoints
{
    public static void MapWindowLayoutsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/layouts", GetAllAsync);
        endpoints.MapGet("/api/layouts/capture", CaptureAsync);
        endpoints.MapPost("/api/layouts", CreateAsync);
        endpoints.MapPost("/api/layouts/{id}/rename", RenameAsync);
        endpoints.MapPost("/api/layouts/{id}/launch", LaunchAsync);
        endpoints.MapDelete("/api/layouts/{id}", DeleteAsync);
    }

    private static async Task<IResult> GetAllAsync(
        IWindowLayoutsRepository layoutsRepository,
        IWorkCollectionsRepository collectionsRepository,
        CancellationToken ct)
    {
        var layoutsTask = layoutsRepository.GetAllAsync(ct).AsTask();
        var collectionsTask = collectionsRepository.GetAllAsync(ct).AsTask();
        await Task.WhenAll(layoutsTask, collectionsTask);
        var collections = (await collectionsTask).ToDictionary(
            collection => collection.Id,
            StringComparer.Ordinal);
        return Results.Ok(new
        {
            layouts = (await layoutsTask).Select(layout => new
            {
                id = layout.Id,
                name = layout.Name,
                createdAt = layout.CreatedAt,
                updatedAt = layout.UpdatedAt,
                windows = layout.Slots.Select(slot => new
                {
                    id = slot.Id,
                    collectionId = slot.CollectionId,
                    collectionName = collections.TryGetValue(
                        slot.CollectionId,
                        out var collection)
                            ? collection.Name
                            : null,
                    capturedWindowTitle = slot.CapturedWindowTitle,
                    monitorDeviceName = slot.MonitorDeviceName,
                    capturedBounds = slot.CapturedBounds,
                    normalizedBounds = slot.NormalizedBounds,
                    windowState = slot.WindowState.ToString().ToLowerInvariant(),
                    zOrder = slot.ZOrder,
                }),
            }),
        });
    }

    private static async Task<IResult> CaptureAsync(
        IWindowLayoutService service,
        CancellationToken ct)
    {
        var capture = await service.CaptureAsync(ct);
        return Results.Ok(new
        {
            available = capture.IsAvailable,
            unavailableReason = capture.UnavailableReason,
            windows = capture.Windows.Select(candidate => new
            {
                handle = $"0x{candidate.Window.Handle:X}",
                candidate.Window.Title,
                candidate.Window.ZOrder,
                candidate.Window.Bounds,
                state = candidate.Window.State.ToString().ToLowerInvariant(),
                monitor = candidate.Window.Monitor,
                candidate.SuggestedCollectionId,
            }),
        });
    }

    private static async Task<IResult> CreateAsync(
        WindowLayoutCreateRequest request,
        IWindowLayoutsRepository layoutsRepository,
        IWorkCollectionsRepository collectionsRepository,
        CancellationToken ct)
    {
        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return Results.BadRequest("A Layout name is required.");
        if (request.Windows is not { Length: > 0 })
            return Results.BadRequest("Select at least one captured window.");

        var collections = await collectionsRepository.GetAllAsync(ct);
        var collectionIds = collections
            .Select(collection => collection.Id)
            .ToHashSet(StringComparer.Ordinal);
        var slots = new List<WindowLayoutSlotDefinition>(request.Windows.Length);
        for (var index = 0; index < request.Windows.Length; index++)
        {
            var window = request.Windows[index];
            if (!collectionIds.Contains(window.CollectionId))
                return Results.BadRequest("A selected Collection no longer exists.");
            if (!TryParseState(window.WindowState, out var state))
                return Results.BadRequest("A captured window has an invalid state.");
            if (window.CapturedWorkArea.Width <= 0 ||
                window.CapturedWorkArea.Height <= 0 ||
                window.CapturedBounds.Width <= 0 ||
                window.CapturedBounds.Height <= 0 ||
                string.IsNullOrWhiteSpace(window.MonitorDeviceName))
            {
                return Results.BadRequest("A captured window has invalid placement data.");
            }

            slots.Add(new WindowLayoutSlotDefinition(
                index,
                window.CollectionId,
                string.IsNullOrWhiteSpace(window.CapturedWindowTitle)
                    ? null
                    : window.CapturedWindowTitle.Trim(),
                window.MonitorDeviceName,
                window.MonitorIsPrimary,
                window.CapturedWorkArea,
                window.CapturedBounds,
                Normalize(window.CapturedBounds, window.CapturedWorkArea),
                state,
                window.ZOrder,
                WindowLayoutDesktopPolicy.Current));
        }

        try
        {
            var layout = await layoutsRepository.CreateAsync(
                name,
                slots,
                DateTimeOffset.UtcNow,
                ct);
            return Results.Ok(new
            {
                id = layout.Id,
                name = layout.Name,
                windowCount = layout.Slots.Count,
            });
        }
        catch (WindowLayoutNameConflictException exception)
        {
            return Results.Conflict(exception.Message);
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(exception.Message);
        }
    }

    private static async Task<IResult> RenameAsync(
        string id,
        WindowLayoutRenameRequest request,
        IWindowLayoutsRepository repository,
        CancellationToken ct)
    {
        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return Results.BadRequest("A Layout name is required.");
        try
        {
            return await repository.RenameAsync(
                id,
                name,
                DateTimeOffset.UtcNow,
                ct)
                    ? Results.Ok(new { id, name })
                    : Results.NotFound("Layout not found");
        }
        catch (WindowLayoutNameConflictException exception)
        {
            return Results.Conflict(exception.Message);
        }
    }

    private static async Task<IResult> LaunchAsync(
        string id,
        WindowLayoutLaunchRequest request,
        IWindowLayoutsRepository repository,
        IWindowLayoutService service,
        CancellationToken ct)
    {
        var layout = await repository.GetByIdAsync(id, ct);
        if (layout is null)
            return Results.NotFound("Layout not found");

        var result = await service.LaunchAsync(layout, request.Force, ct);
        if (result.Collisions.Count > 0)
        {
            return Results.Json(
                new
                {
                    error = "directory-collision",
                    message = "Two or more Layout sessions would share a working directory.",
                    collisions = result.Collisions.Select(collision => new
                    {
                        sessionId = collision.SessionId,
                        directory = collision.Directory,
                        occupyingSessionId = collision.OccupyingSessionId,
                        occupyingSessionName = collision.OccupyingSessionName,
                        occupyingIsLive = collision.OccupyingIsLive,
                        description = collision.Describe(),
                    }),
                },
                statusCode: StatusCodes.Status409Conflict);
        }
        if (!result.PreflightPassed)
        {
            return Results.Json(
                new
                {
                    error = "layout-preflight",
                    message = "The Layout cannot be launched safely.",
                    issues = result.Issues,
                },
                statusCode: StatusCodes.Status409Conflict);
        }

        return Results.Ok(new
        {
            success = result.Success,
            windows = result.Windows.Select(window => new
            {
                window.CollectionId,
                window.CollectionName,
                window.Success,
                window.LaunchedSessions,
                window.WindowHandle,
                adaptation = window.Adaptation?.ToString().ToLowerInvariant(),
                window.RequestedBounds,
                window.ActualBounds,
                failures = window.Failures.Select(failure => new
                {
                    failure.SessionId,
                    failure.Reason,
                }),
                window.Error,
            }),
        });
    }

    private static async Task<IResult> DeleteAsync(
        string id,
        IWindowLayoutsRepository repository,
        CancellationToken ct) =>
        await repository.DeleteAsync(id, ct)
            ? Results.NoContent()
            : Results.NotFound("Layout not found");

    private static NormalizedWindowRectangle Normalize(
        WindowRectangle bounds,
        WindowRectangle workArea) =>
        new(
            (bounds.X - workArea.X) / (double)workArea.Width,
            (bounds.Y - workArea.Y) / (double)workArea.Height,
            bounds.Width / (double)workArea.Width,
            bounds.Height / (double)workArea.Height);

    private static bool TryParseState(
        string? value,
        out WindowLayoutState state) =>
        Enum.TryParse(value, ignoreCase: true, out state);

    internal sealed record WindowLayoutCreateRequest(
        string? Name,
        WindowLayoutCapturedWindowRequest[]? Windows);

    internal sealed record WindowLayoutCapturedWindowRequest(
        string CollectionId,
        string? CapturedWindowTitle,
        string MonitorDeviceName,
        bool MonitorIsPrimary,
        WindowRectangle CapturedWorkArea,
        WindowRectangle CapturedBounds,
        string? WindowState,
        int ZOrder);

    internal sealed record WindowLayoutRenameRequest(string? Name);

    internal sealed record WindowLayoutLaunchRequest(bool Force = false);
}
