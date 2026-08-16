using NexusLabs.Narnia.Core.Models;
using NexusLabs.Narnia.Core.Repositories;

namespace NexusLabs.Narnia.Web;

internal static class WindowLayoutsEndpoints
{
    public static void MapWindowLayoutsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/layouts", GetAllAsync);
        endpoints.MapGet("/api/layouts/capture", CaptureAsync);
        endpoints.MapGet("/api/layouts/catalog/sessions", SearchSessionsAsync);
        endpoints.MapPost("/api/layouts", CreateAsync);
        endpoints.MapPost("/api/layouts/blank", CreateBlankAsync);
        endpoints.MapPost("/api/layouts/{id}/rename", RenameAsync);
        endpoints.MapPost("/api/layouts/{id}/definition", ReplaceDefinitionAsync);
        endpoints.MapPost("/api/layouts/{id}/launch", LaunchAsync);
        endpoints.MapDelete("/api/layouts/{id}", DeleteAsync);
    }

    private static async Task<IResult> SearchSessionsAsync(
        string? q,
        ISessionRepository sessionsRepository,
        IRecordedSessionRepository recordedSessionsRepository,
        ISessionSearch sessionSearch,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(q))
        {
            var recent = await sessionsRepository.ListRecentAsync(50, ct: ct);
            var recordedRecent = await recordedSessionsRepository.GetByIdsAsync(
                recent.Select(session => session.Id).ToArray(),
                ct);
            return Results.Ok(new
            {
                sessions = recent.Select(session => new
                {
                    id = session.Id,
                    name = session.Summary ?? Short(session.Id),
                    session.Repository,
                    session.UpdatedAt,
                    recordedName = recordedRecent.TryGetValue(session.Id, out var raw)
                        ? raw.Summary
                        : null,
                    recordedRepository = recordedRecent.TryGetValue(session.Id, out raw)
                        ? raw.Repository
                        : null,
                }),
            });
        }

        var results = await sessionSearch.SearchAsync(q.Trim(), 50, ct);
        var ids = results
            .Select(result => result.SessionId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var sessions = await sessionsRepository.GetByIdsAsync(ids, ct);
        var recorded = await recordedSessionsRepository.GetByIdsAsync(ids, ct);
        return Results.Ok(new
        {
            sessions = ids
                .Where(sessions.ContainsKey)
                .Select(id =>
                {
                    var session = sessions[id];
                    return new
                    {
                        id,
                        name = session.Summary ?? Short(id),
                        session.Repository,
                        session.UpdatedAt,
                        recordedName = recorded.TryGetValue(id, out var raw)
                            ? raw.Summary
                            : null,
                        recordedRepository = recorded.TryGetValue(id, out raw)
                            ? raw.Repository
                            : null,
                    };
                }),
        });
    }

    private static async Task<IResult> GetAllAsync(
        IWindowLayoutsRepository layoutsRepository,
        IWorkCollectionsRepository collectionsRepository,
        ISessionRepository sessionsRepository,
        IRecordedSessionRepository recordedSessionsRepository,
        CancellationToken ct)
    {
        var layoutsTask = layoutsRepository.GetAllAsync(ct).AsTask();
        var collectionsTask = collectionsRepository.GetAllAsync(ct).AsTask();
        await Task.WhenAll(layoutsTask, collectionsTask);
        var layouts = await layoutsTask;
        var collections = (await collectionsTask).ToDictionary(
            collection => collection.Id,
            StringComparer.Ordinal);
        var sessions = await sessionsRepository.GetByIdsAsync(
            layouts
                .SelectMany(layout => layout.Slots)
                .Where(slot => slot.SessionId is not null)
                .Select(slot => slot.SessionId!)
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
            ct);
        var recordedSessions = await recordedSessionsRepository.GetByIdsAsync(
            sessions.Keys.ToArray(),
            ct);
        return Results.Ok(new
        {
            layouts = layouts.Select(layout => new
            {
                id = layout.Id,
                name = layout.Name,
                createdAt = layout.CreatedAt,
                updatedAt = layout.UpdatedAt,
                windows = layout.Slots.Select(slot => new
                {
                    id = slot.Id,
                    contentKind = slot.ContentKind.ToString().ToLowerInvariant(),
                    collectionId = slot.CollectionId,
                    sessionId = slot.SessionId,
                    collectionName = slot.CollectionId is not null &&
                        collections.TryGetValue(
                        slot.CollectionId,
                        out var collection)
                            ? collection.Name
                            : null,
                    sessionName = slot.SessionId is not null &&
                        sessions.TryGetValue(slot.SessionId, out var session)
                            ? session.Summary ?? Short(session.Id)
                            : null,
                    recordedSessionName = slot.SessionId is not null &&
                        recordedSessions.TryGetValue(slot.SessionId, out var recorded)
                            ? recorded.Summary
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
        var monitors = request.Windows
            .GroupBy(
                window => window.MonitorDeviceName,
                StringComparer.OrdinalIgnoreCase)
            .Select((group, index) =>
            {
                var monitor = group.First();
                return new WindowLayoutMonitorDefinition(
                    index,
                    monitor.MonitorDeviceName,
                    monitor.MonitorIsPrimary,
                    monitor.CapturedMonitorBounds,
                    monitor.CapturedWorkArea);
            })
            .ToArray();
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
                WindowLayoutContentKind.Collection,
                window.CollectionId,
                null,
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
                monitors,
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

    private static async Task<IResult> CreateBlankAsync(
        WindowLayoutBlankCreateRequest request,
        IWindowLayoutsRepository repository,
        IWindowLayoutPlatform platform,
        CancellationToken ct)
    {
        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return Results.BadRequest("A Layout name is required.");

        var capture = platform.Capture();
        if (!capture.IsAvailable || capture.Monitors.Count == 0)
        {
            return Results.BadRequest(
                capture.UnavailableReason ?? "No desktop monitors are available.");
        }

        var monitors = capture.Monitors
            .OrderBy(monitor => monitor.Bounds.X)
            .ThenBy(monitor => monitor.Bounds.Y)
            .Select((monitor, index) => new WindowLayoutMonitorDefinition(
                index,
                monitor.DeviceName,
                monitor.IsPrimary,
                monitor.Bounds,
                monitor.WorkArea))
            .ToArray();
        try
        {
            var layout = await repository.CreateAsync(
                name,
                monitors,
                [],
                DateTimeOffset.UtcNow,
                ct);
            return Results.Ok(new { id = layout.Id, name = layout.Name });
        }
        catch (WindowLayoutNameConflictException exception)
        {
            return Results.Conflict(exception.Message);
        }
    }

    private static async Task<IResult> ReplaceDefinitionAsync(
        string id,
        WindowLayoutDefinitionRequest request,
        IWindowLayoutsRepository layoutsRepository,
        IWorkCollectionsRepository collectionsRepository,
        ISessionRepository sessionsRepository,
        CancellationToken ct)
    {
        var layout = await layoutsRepository.GetByIdAsync(id, ct);
        if (layout is null)
            return Results.NotFound("Layout not found");
        if (request.Windows is null)
            return Results.BadRequest("Layout windows are required.");

        var monitors = layout.Monitors.ToDictionary(
            monitor => monitor.DeviceName,
            StringComparer.OrdinalIgnoreCase);
        var collections = (await collectionsRepository.GetAllAsync(ct))
            .Select(collection => collection.Id)
            .ToHashSet(StringComparer.Ordinal);
        var requestedSessionIds = request.Windows
            .Where(window => string.Equals(
                window.ContentKind,
                "session",
                StringComparison.OrdinalIgnoreCase))
            .Select(window => window.ContentId)
            .ToArray();
        var sessions = await sessionsRepository.GetByIdsAsync(requestedSessionIds, ct);
        var slots = new List<WindowLayoutSlotDefinition>(request.Windows.Length);
        var contentKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < request.Windows.Length; index++)
        {
            var window = request.Windows[index];
            if (!TryParseContentKind(window.ContentKind, out var contentKind))
                return Results.BadRequest("A Layout window has an invalid content kind.");
            if (!monitors.TryGetValue(window.MonitorDeviceName, out var monitor))
                return Results.BadRequest("A Layout window references an unavailable monitor.");
            if (string.IsNullOrWhiteSpace(window.ContentId) ||
                !contentKeys.Add($"{contentKind}:{window.ContentId}"))
            {
                return Results.BadRequest(
                    "A Collection or session can appear only once in a Layout.");
            }
            if (contentKind == WindowLayoutContentKind.Collection &&
                !collections.Contains(window.ContentId))
            {
                return Results.BadRequest("A selected Collection no longer exists.");
            }
            if (contentKind == WindowLayoutContentKind.Session &&
                !sessions.ContainsKey(window.ContentId))
            {
                return Results.BadRequest("A selected session no longer exists.");
            }
            if (!ValidNormalized(window))
                return Results.BadRequest("A Layout window has invalid placement.");

            var normalized = new NormalizedWindowRectangle(
                window.X,
                window.Y,
                window.Width,
                window.Height);
            var bounds = ToBounds(normalized, monitor.CapturedWorkArea);
            slots.Add(new WindowLayoutSlotDefinition(
                index,
                contentKind,
                contentKind == WindowLayoutContentKind.Collection
                    ? window.ContentId
                    : null,
                contentKind == WindowLayoutContentKind.Session
                    ? window.ContentId
                    : null,
                string.IsNullOrWhiteSpace(window.Title) ? null : window.Title.Trim(),
                monitor.DeviceName,
                monitor.IsPrimary,
                monitor.CapturedWorkArea,
                bounds,
                normalized,
                WindowLayoutState.Normal,
                window.ZOrder,
                WindowLayoutDesktopPolicy.Current));
        }

        return await layoutsRepository.ReplaceDefinitionAsync(
            id,
            layout.Monitors,
            slots,
            DateTimeOffset.UtcNow,
            ct)
                ? Results.Ok(new { id, windowCount = slots.Count })
                : Results.NotFound("Layout not found");
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
                contentKind = window.ContentKind.ToString().ToLowerInvariant(),
                window.ContentId,
                window.ContentName,
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

    private static WindowRectangle ToBounds(
        NormalizedWindowRectangle normalized,
        WindowRectangle workArea) =>
        new(
            workArea.X + (int)Math.Round(normalized.X * workArea.Width),
            workArea.Y + (int)Math.Round(normalized.Y * workArea.Height),
            (int)Math.Round(normalized.Width * workArea.Width),
            (int)Math.Round(normalized.Height * workArea.Height));

    private static bool ValidNormalized(WindowLayoutEditorWindowRequest window) =>
        IsFinite(window.X) &&
        IsFinite(window.Y) &&
        IsFinite(window.Width) &&
        IsFinite(window.Height) &&
        window.X >= 0 &&
        window.Y >= 0 &&
        window.Width > 0 &&
        window.Height > 0 &&
        window.X + window.Width <= 1.0001 &&
        window.Y + window.Height <= 1.0001;

    private static bool IsFinite(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value);

    private static bool TryParseState(
        string? value,
        out WindowLayoutState state) =>
        Enum.TryParse(value, ignoreCase: true, out state);

    private static bool TryParseContentKind(
        string? value,
        out WindowLayoutContentKind kind) =>
        Enum.TryParse(value, ignoreCase: true, out kind);

    private static string Short(string id) =>
        id.Length > 8 ? id[..8] : id;

    internal sealed record WindowLayoutCreateRequest(
        string? Name,
        WindowLayoutCapturedWindowRequest[]? Windows);

    internal sealed record WindowLayoutCapturedWindowRequest(
        string CollectionId,
        string? CapturedWindowTitle,
        string MonitorDeviceName,
        bool MonitorIsPrimary,
        WindowRectangle CapturedMonitorBounds,
        WindowRectangle CapturedWorkArea,
        WindowRectangle CapturedBounds,
        string? WindowState,
        int ZOrder);

    internal sealed record WindowLayoutRenameRequest(string? Name);

    internal sealed record WindowLayoutBlankCreateRequest(string? Name);

    internal sealed record WindowLayoutDefinitionRequest(
        WindowLayoutEditorWindowRequest[]? Windows);

    internal sealed record WindowLayoutEditorWindowRequest(
        string? ContentKind,
        string ContentId,
        string MonitorDeviceName,
        string? Title,
        double X,
        double Y,
        double Width,
        double Height,
        int ZOrder);

    internal sealed record WindowLayoutLaunchRequest(bool Force = false);
}
