using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using NexusLabs.Narnia.Core.Repositories;

namespace NexusLabs.Narnia.Web.Tests;

public sealed class SessionOverridesEndpointsTests
{
    [Theory]
    [InlineData(@"C:\dev\needlr")]
    [InlineData("../needlr")]
    [InlineData("./needlr")]
    [InlineData("ncosentino/..")]
    [InlineData("ncosentino/.")]
    public async Task SaveOverride_PathShapedRepository_ReturnsValidationError(string repository)
    {
        using var factory = new NarniaWebAppFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/sessions/session-1/overrides",
            MakeRequest(repository),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("owner/repository", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaveOverride_RepositorySlug_PersistsValue()
    {
        using var factory = new NarniaWebAppFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/sessions/session-1/overrides",
            MakeRequest(repository: "ncosentino/needlr"),
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
        var repository = factory.Services.GetRequiredService<ISessionOverridesRepository>();
        var saved = await repository.GetOverrideAsync(
            "session-1",
            TestContext.Current.CancellationToken);
        Assert.NotNull(saved);
        Assert.Equal("ncosentino/needlr", saved.Repository);
    }

    private static object MakeRequest(string repository) => new
    {
        DisplayName = (string?)null,
        Repository = repository,
        Branch = (string?)null,
        Notes = (string?)null,
        LocalPath = (string?)null,
        TerminalTitle = (string?)null,
    };
}
