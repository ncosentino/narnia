using NexusLabs.Narnia.Core.Models;
using NexusLabs.Narnia.Core.Repositories;

namespace NexusLabs.Narnia.Web;

internal static class WorkCollectionsEndpoints
{
    public static void MapWorkCollectionsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/collections", GetAllAsync);
        endpoints.MapGet("/api/collections/{id}", GetByIdAsync);
        endpoints.MapPost("/api/collections", CreateAsync);
        endpoints.MapPost("/api/collections/{id}/rename", RenameAsync);
        endpoints.MapPost("/api/collections/{id}/sessions", AddSessionsAsync);
        endpoints.MapPost("/api/collections/{id}/sessions/remove", RemoveSessionsAsync);
        endpoints.MapDelete("/api/collections/{id}", DeleteAsync);
    }

    private static async Task<IResult> GetAllAsync(
        IWorkCollectionsRepository repository,
        CancellationToken ct)
    {
        var collections = await repository.GetAllAsync(ct);
        return Results.Ok(new
        {
            collections = collections.Select(collection => new
            {
                id = collection.Id,
                name = collection.Name,
                createdAt = collection.CreatedAt,
                updatedAt = collection.UpdatedAt,
                memberCount = collection.Members.Count,
            }),
        });
    }

    private static async Task<IResult> GetByIdAsync(
        string id,
        IWorkCollectionsRepository collectionsRepository,
        ISessionRepository sessionsRepository,
        CancellationToken ct)
    {
        var collection = await collectionsRepository.GetByIdAsync(id, ct);
        if (collection is null)
            return Results.NotFound("Collection not found");

        var sessionsById = await sessionsRepository.GetByIdsAsync(
            collection.Members.Select(member => member.SessionId).ToArray(),
            ct);
        return Results.Ok(new
        {
            collection = new
            {
                id = collection.Id,
                name = collection.Name,
                createdAt = collection.CreatedAt,
                updatedAt = collection.UpdatedAt,
                members = collection.Members.Select(member =>
                {
                    sessionsById.TryGetValue(member.SessionId, out var session);
                    return new
                    {
                        sessionId = member.SessionId,
                        addedAt = member.AddedAt,
                        summary = session?.Summary,
                        repository = session?.Repository,
                        branch = session?.Branch,
                        workingDirectory = session?.Cwd,
                        updatedAt = session?.UpdatedAt,
                    };
                }),
            },
        });
    }

    private static async Task<IResult> CreateAsync(
        WorkCollectionCreateRequest request,
        IWorkCollectionsRepository repository,
        CancellationToken ct)
    {
        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return Results.BadRequest("A collection name is required.");

        try
        {
            var collection = await repository.CreateAsync(
                name,
                request.SessionIds ?? [],
                DateTimeOffset.UtcNow,
                ct);
            return Results.Ok(new
            {
                id = collection.Id,
                name = collection.Name,
                memberCount = collection.Members.Count,
            });
        }
        catch (WorkCollectionNameConflictException exception)
        {
            return Results.Conflict(exception.Message);
        }
    }

    private static async Task<IResult> RenameAsync(
        string id,
        WorkCollectionRenameRequest request,
        IWorkCollectionsRepository repository,
        CancellationToken ct)
    {
        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return Results.BadRequest("A collection name is required.");

        try
        {
            return await repository.RenameAsync(id, name, DateTimeOffset.UtcNow, ct)
                ? Results.Ok(new { id, name })
                : Results.NotFound("Collection not found");
        }
        catch (WorkCollectionNameConflictException exception)
        {
            return Results.Conflict(exception.Message);
        }
    }

    private static Task<IResult> AddSessionsAsync(
        string id,
        WorkCollectionMembershipRequest request,
        IWorkCollectionsRepository repository,
        CancellationToken ct) =>
        ChangeMembershipAsync(id, request, repository.AddSessionsAsync, ct);

    private static Task<IResult> RemoveSessionsAsync(
        string id,
        WorkCollectionMembershipRequest request,
        IWorkCollectionsRepository repository,
        CancellationToken ct) =>
        ChangeMembershipAsync(id, request, repository.RemoveSessionsAsync, ct);

    private static async Task<IResult> ChangeMembershipAsync(
        string id,
        WorkCollectionMembershipRequest request,
        Func<string, IReadOnlyCollection<string>, DateTimeOffset, CancellationToken, ValueTask<int?>>
            changeMembership,
        CancellationToken ct)
    {
        if (request.SessionIds is not { Length: > 0 })
            return Results.BadRequest("Select at least one session.");

        var changed = await changeMembership(id, request.SessionIds, DateTimeOffset.UtcNow, ct);
        if (changed is null)
            return Results.NotFound("Collection not found");

        return Results.Ok(new WorkCollectionMembershipChangeResponse(id, changed.Value));
    }

    private static async Task<IResult> DeleteAsync(
        string id,
        IWorkCollectionsRepository repository,
        CancellationToken ct) =>
        await repository.DeleteAsync(id, ct)
            ? Results.NoContent()
            : Results.NotFound("Collection not found");

    internal sealed record WorkCollectionCreateRequest(string? Name, string[]? SessionIds);

    internal sealed record WorkCollectionRenameRequest(string? Name);

    internal sealed record WorkCollectionMembershipRequest(string[] SessionIds);

    internal sealed record WorkCollectionMembershipChangeResponse(string Id, int Changed);
}
