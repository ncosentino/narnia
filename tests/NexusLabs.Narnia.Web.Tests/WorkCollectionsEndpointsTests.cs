using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using NexusLabs.Narnia.Core.Models;
using NexusLabs.Narnia.Core.Repositories;

namespace NexusLabs.Narnia.Web.Tests;

public sealed class WorkCollectionsEndpointsTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 17, 12, 0, 0, TimeSpan.Zero);

    private const string Session1 = "11111111-1111-4111-8111-111111111111";
    private const string Session2 = "22222222-2222-4222-8222-222222222222";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CreateCollection_AllowsEmptyMembershipAndTrimsName()
    {
        using var factory = new NarniaWebAppFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/collections",
            new { name = "  BrandGhost  ", sessionIds = Array.Empty<string>() },
            Ct);

        response.EnsureSuccessStatusCode();
        var collection = Assert.Single(await factory.WorkCollectionsRepository.GetAllAsync(Ct));
        Assert.Equal("BrandGhost", collection.Name);
        Assert.Empty(collection.Members);
    }

    [Fact]
    public async Task CreateCollection_DuplicateNameIgnoringCase_Returns409()
    {
        using var factory = new NarniaWebAppFactory();
        var client = factory.CreateClient();
        await factory.WorkCollectionsRepository.CreateAsync("MCP Tools", [], Now, Ct);

        var response = await client.PostAsJsonAsync(
            "/api/collections",
            new { name = "mcp tools", sessionIds = Array.Empty<string>() },
            Ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task GetCollections_ReturnsAlphabeticalSummaries()
    {
        using var factory = new NarniaWebAppFactory();
        await factory.WorkCollectionsRepository.CreateAsync("Zulu", [Session1], Now, Ct);
        await factory.WorkCollectionsRepository.CreateAsync("Alpha", [], Now, Ct);

        var response = await factory.CreateClient()
            .GetFromJsonAsync<CollectionsResponse>("/api/collections", Ct);

        Assert.NotNull(response);
        Assert.Equal(["Alpha", "Zulu"], response!.Collections.Select(collection => collection.Name));
        Assert.Equal(1, response.Collections[1].MemberCount);
    }

    [Fact]
    public async Task GetCollection_EnrichesMembersWithBatchSessionLookup()
    {
        using var factory = new NarniaWebAppFactory();
        var collection = await factory.WorkCollectionsRepository.CreateAsync(
            "BrandGhost",
            [Session1],
            Now,
            Ct);
        var session = new Session(
            Session1,
            @"C:\dev\brandghost",
            "brandghost/brandghost",
            "main",
            "BrandGhost session",
            null,
            Now,
            Now);
        factory.SessionRepository
            .Setup(repository => repository.GetByIdsAsync(
                It.Is<IReadOnlyCollection<string>>(sessionIds =>
                    sessionIds.SequenceEqual(new[] { Session1 })),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, Session>(StringComparer.Ordinal)
            {
                [Session1] = session,
            });

        var response = await factory.CreateClient()
            .GetFromJsonAsync<CollectionResponse>($"/api/collections/{collection.Id}", Ct);

        Assert.NotNull(response);
        var member = Assert.Single(response!.Collection.Members);
        Assert.Equal("BrandGhost session", member.Summary);
        Assert.Equal("brandghost/brandghost", member.Repository);
        Assert.Equal(@"C:\dev\brandghost", member.WorkingDirectory);
    }

    [Fact]
    public async Task AddAndRemoveSessions_ChangeOnlyRequestedMemberships()
    {
        using var factory = new NarniaWebAppFactory();
        var collection = await factory.WorkCollectionsRepository.CreateAsync(
            "BrandGhost",
            [Session1],
            Now,
            Ct);
        var client = factory.CreateClient();

        var addResponse = await client.PostAsJsonAsync(
            $"/api/collections/{collection.Id}/sessions",
            new { sessionIds = new[] { Session1, Session2 } },
            Ct);
        var removeResponse = await client.PostAsJsonAsync(
            $"/api/collections/{collection.Id}/sessions/remove",
            new { sessionIds = new[] { Session1 } },
            Ct);

        addResponse.EnsureSuccessStatusCode();
        removeResponse.EnsureSuccessStatusCode();
        var updated = await factory.WorkCollectionsRepository.GetByIdAsync(collection.Id, Ct);
        Assert.Equal(Session2, Assert.Single(updated!.Members).SessionId);
    }

    [Fact]
    public async Task MembershipChange_MissingCollection_Returns404()
    {
        using var factory = new NarniaWebAppFactory();
        var response = await factory.CreateClient().PostAsJsonAsync(
            $"/api/collections/{Guid.NewGuid()}/sessions",
            new { sessionIds = new[] { Session1 } },
            Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RenameAndDeleteCollection_PersistChanges()
    {
        using var factory = new NarniaWebAppFactory();
        var collection = await factory.WorkCollectionsRepository.CreateAsync(
            "Old",
            [Session1],
            Now,
            Ct);
        var client = factory.CreateClient();

        var renameResponse = await client.PostAsJsonAsync(
            $"/api/collections/{collection.Id}/rename",
            new { name = "New" },
            Ct);
        var renamed = await factory.WorkCollectionsRepository.GetByIdAsync(collection.Id, Ct);
        var deleteResponse = await client.DeleteAsync($"/api/collections/{collection.Id}", Ct);

        renameResponse.EnsureSuccessStatusCode();
        Assert.Equal("New", renamed!.Name);
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
        Assert.Null(await factory.WorkCollectionsRepository.GetByIdAsync(collection.Id, Ct));
    }

    [Fact]
    public async Task OpenCollection_LaunchesMoreThanBulkSelectionLimit()
    {
        using var factory = new NarniaWebAppFactory();
        var sessionIds = Enumerable.Range(0, 21)
            .Select(_ => Guid.NewGuid().ToString())
            .ToArray();
        var collection = await factory.WorkCollectionsRepository.CreateAsync(
            "Large collection",
            sessionIds,
            Now,
            Ct);
        factory.SessionRepository
            .Setup(repository => repository.GetByIdAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string sessionId, CancellationToken _) => new Session(
                sessionId,
                Path.GetTempPath(),
                null,
                null,
                "Collection session",
                null,
                Now,
                Now));
        await factory.Services.GetRequiredService<INarniaSettingsRepository>()
            .SetAsync("shell_path", "pwsh.exe", Ct);

        using var response = await factory.CreateClient().PostAsJsonAsync(
            $"/api/collections/{collection.Id}/open",
            new { separateWindows = true },
            Ct);

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<CollectionOpenResponse>(Ct);
        Assert.Equal(21, body!.Launched.Count);
        Assert.Empty(body.Failed);
        factory.ProcessLauncher.Verify(
            launcher => launcher.Start(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>()),
            Times.Exactly(21));
    }

    [Fact]
    public async Task OpenCollection_MissingCollection_Returns404()
    {
        using var factory = new NarniaWebAppFactory();

        using var response = await factory.CreateClient().PostAsJsonAsync(
            $"/api/collections/{Guid.NewGuid()}/open",
            new { separateWindows = false },
            Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        factory.ProcessLauncher.VerifyNoOtherCalls();
    }

    private sealed record CollectionsResponse(List<CollectionSummaryDto> Collections);

    private sealed record CollectionSummaryDto(string Id, string Name, int MemberCount);

    private sealed record CollectionResponse(CollectionDetailDto Collection);

    private sealed record CollectionDetailDto(
        string Id,
        string Name,
        List<CollectionMemberDto> Members);

    private sealed record CollectionMemberDto(
        string SessionId,
        string? Summary,
        string? Repository,
        string? WorkingDirectory);

    private sealed record CollectionOpenResponse(
        List<LaunchedSessionDto> Launched,
        List<LaunchFailureDto> Failed);

    private sealed record LaunchedSessionDto(string SessionId);

    private sealed record LaunchFailureDto(string SessionId, string Reason);
}
