using System.Net;
using System.Net.Http.Json;

namespace NexusLabs.Narnia.Web.Tests;

public sealed class SessionGroupsEndpointsTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 6, 22, 12, 0, 0, TimeSpan.Zero);

    private const string SessionId = "11111111-1111-4111-8111-111111111111";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("/api/session-groups")]
    [InlineData("/api/groups")]
    [InlineData("/api/session-groups/legacy-group/reopen")]
    [InlineData("/api/groups/legacy-group/rename")]
    public async Task SessionGroupApis_ReturnGoneWithoutChangingLegacyData(string route)
    {
        using var factory = new NarniaWebAppFactory();
        var group = await factory.GroupsRepository.CreateAsync(
            "Legacy group",
            [SessionId],
            Now,
            Ct);
        var client = factory.CreateClient();

        using var response = route.EndsWith("/reopen", StringComparison.Ordinal) ||
            route.EndsWith("/rename", StringComparison.Ordinal)
                ? await client.PostAsJsonAsync(route, new { }, Ct)
                : await client.GetAsync(route, Ct);

        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<RetiredResponse>(Ct);
        Assert.Contains("Use Collections", body!.Message, StringComparison.Ordinal);

        var preserved = await factory.GroupsRepository.GetByIdAsync(group.Id, Ct);
        Assert.NotNull(preserved);
        Assert.Equal([SessionId], preserved!.Members.Select(member => member.SessionId));
    }

    private sealed record RetiredResponse(string Message);
}
