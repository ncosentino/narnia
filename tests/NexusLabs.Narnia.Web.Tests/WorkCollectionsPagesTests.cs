using NexusLabs.Narnia.Core.Models;

namespace NexusLabs.Narnia.Web.Tests;

public sealed class WorkCollectionsPagesTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 17, 12, 0, 0, TimeSpan.Zero);

    private const string SessionId = "11111111-1111-4111-8111-111111111111";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CollectionsPage_LinksToCollectionDetails()
    {
        using var factory = new NarniaWebAppFactory();
        var collection = await factory.WorkCollectionsRepository.CreateAsync(
            "BrandGhost",
            [],
            Now,
            Ct);

        var html = await factory.CreateClient().GetStringAsync("/collections", Ct);

        Assert.Contains($"""href="/collections/{collection.Id}">BrandGhost</a>""", html);
    }

    [Fact]
    public async Task CollectionDetailPage_LinksToMemberSession()
    {
        using var factory = new NarniaWebAppFactory();
        var collection = await factory.WorkCollectionsRepository.CreateAsync(
            "BrandGhost",
            [SessionId],
            Now,
            Ct);
        var session = new Session(
            SessionId,
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
                    sessionIds.SequenceEqual(new[] { SessionId })),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, Session>(StringComparer.Ordinal)
            {
                [SessionId] = session,
            });

        var html = await factory.CreateClient()
            .GetStringAsync($"/collections/{collection.Id}", Ct);

        Assert.Contains($"""href="/sessions/{SessionId}">BrandGhost session</a>""", html);
        Assert.Contains(
            "onclick=\"narniaLaunchSelectedCollectionSessions(this)\"",
            html);
        Assert.Contains(
            "onclick=\"narniaSaveSelectedCollectionSessionsAsSessionGroup(this)\"",
            html);
    }
}
