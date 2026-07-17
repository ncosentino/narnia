using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using NexusLabs.Narnia.Core.Models;
using NexusLabs.Narnia.Core.Repositories;

namespace NexusLabs.Narnia.Web.Tests;

public sealed class SessionGroupsEndpointsTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 22, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Session1 = "11111111-1111-4111-8111-111111111111";
    private const string Session2 = "22222222-2222-4222-8222-222222222222";

    [Theory]
    [InlineData("/api/session-groups")]
    [InlineData("/api/groups")]
    public async Task CreateSessionGroup_CanonicalAndLegacyRoutesPersistNamedGroupInOrder(string route)
    {
        using var factory = new NarniaWebAppFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            route, new { name = "  Morning set  ", sessionIds = new[] { Session2, Session1 } }, Ct);

        response.EnsureSuccessStatusCode();
        var groups = await factory.GroupsRepository.GetAllAsync(Ct);
        var group = Assert.Single(groups);
        Assert.Equal("Morning set", group.Name);
        Assert.Equal([Session2, Session1], group.Members.Select(m => m.SessionId));
    }

    [Fact]
    public async Task CreateSessionGroup_EmptyName_Returns400()
    {
        using var factory = new NarniaWebAppFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/session-groups", new { name = "   ", sessionIds = new[] { Session1 } }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await factory.GroupsRepository.GetAllAsync(Ct));
    }

    [Fact]
    public async Task CreateSessionGroup_NoSessions_Returns400()
    {
        using var factory = new NarniaWebAppFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/session-groups", new { name = "Empty", sessionIds = Array.Empty<string>() }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetSessionGroups_ReturnsGroupsWithBatchSessionEnrichment()
    {
        using var factory = new NarniaWebAppFactory();
        var session = new Session(
            Session1,
            @"C:\dev\x",
            "owner/repo",
            "main",
            "My session",
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
        await factory.GroupsRepository.CreateAsync("Group A", [Session1], Now, Ct);

        var client = factory.CreateClient();
        var response = await client.GetFromJsonAsync<SessionGroupsResponse>("/api/session-groups", Ct);

        Assert.NotNull(response);
        var group = Assert.Single(response!.Groups);
        Assert.Equal("Group A", group.Name);
        var member = Assert.Single(group.Members);
        Assert.Equal("My session", member.Summary);
        Assert.Equal("owner/repo", member.Repository);
    }

    [Fact]
    public async Task RenameSessionGroup_ChangesName()
    {
        using var factory = new NarniaWebAppFactory();
        var created = await factory.GroupsRepository.CreateAsync("Old", [Session1], Now, Ct);

        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            $"/api/session-groups/{created.Id}/rename",
            new { name = "New" },
            Ct);

        response.EnsureSuccessStatusCode();
        var fetched = await factory.GroupsRepository.GetByIdAsync(created.Id, Ct);
        Assert.Equal("New", fetched!.Name);
    }

    [Fact]
    public async Task RenameSessionGroup_Missing_Returns404()
    {
        using var factory = new NarniaWebAppFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/api/session-groups/{Guid.NewGuid()}/rename", new { name = "Nope" }, Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task SetMembers_ReplacesMembership()
    {
        using var factory = new NarniaWebAppFactory();
        var created = await factory.GroupsRepository.CreateAsync("Set", [Session1], Now, Ct);

        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            $"/api/session-groups/{created.Id}/members",
            new { sessionIds = new[] { Session2, Session1 } },
            Ct);

        response.EnsureSuccessStatusCode();
        var fetched = await factory.GroupsRepository.GetByIdAsync(created.Id, Ct);
        Assert.Equal([Session2, Session1], fetched!.Members.Select(m => m.SessionId));
    }

    [Fact]
    public async Task DeleteSessionGroup_RemovesIt()
    {
        using var factory = new NarniaWebAppFactory();
        var created = await factory.GroupsRepository.CreateAsync("Doomed", [Session1], Now, Ct);

        var client = factory.CreateClient();
        var response = await client.DeleteAsync($"/api/session-groups/{created.Id}", Ct);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Null(await factory.GroupsRepository.GetByIdAsync(created.Id, Ct));
    }

    [Fact]
    public async Task ReopenSessionGroup_LaunchesEachSessionViaFallback()
    {
        using var factory = new NarniaWebAppFactory();
        await factory.Services.GetRequiredService<INarniaSettingsRepository>()
            .SetAsync("shell_path", "pwsh.exe", Ct);
        var created = await factory.GroupsRepository.CreateAsync("Restore me", [Session1, Session2], Now, Ct);

        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            $"/api/session-groups/{created.Id}/reopen",
            new { separateWindows = true },
            Ct);

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<GroupReopenResponse>(Ct);
        Assert.Equal(2, body!.Reopened);
        // No Windows Terminal in tests → each session launches via the direct fallback.
        factory.ProcessLauncher.Verify(
            p => p.Start(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task ReopenSessionGroup_Missing_Returns404()
    {
        using var factory = new NarniaWebAppFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/api/session-groups/{Guid.NewGuid()}/reopen",
            new { separateWindows = false },
            Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        factory.ProcessLauncher.Verify(
            p => p.Start(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()),
            Times.Never);
    }

    private sealed record SessionGroupsResponse(List<SessionGroupDto> Groups);

    private sealed record SessionGroupDto(string Id, string Name, List<MemberDto> Members);

    private sealed record MemberDto(string SessionId, string? Summary, string? Repository);

    private sealed record GroupReopenResponse(int Reopened);
}
