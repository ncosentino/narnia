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

    [Fact]
    public async Task FavoriteSession_PersistsFavoriteState()
    {
        using var factory = new NarniaWebAppFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/sessions/session-1/favorite",
            new { Favorite = true },
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
        var repository = factory.Services.GetRequiredService<ISessionOverridesRepository>();
        var saved = await repository.GetOverrideAsync(
            "session-1",
            TestContext.Current.CancellationToken);
        Assert.NotNull(saved);
        Assert.True(saved.IsFavorite);
    }

    [Fact]
    public async Task UnfavoriteSession_WithoutOtherOverrides_RemovesEmptyOverride()
    {
        using var factory = new NarniaWebAppFactory();
        using var client = factory.CreateClient();

        var favoriteResponse = await client.PostAsJsonAsync(
            "/api/sessions/session-1/favorite",
            new { Favorite = true },
            TestContext.Current.CancellationToken);
        favoriteResponse.EnsureSuccessStatusCode();

        var unfavoriteResponse = await client.PostAsJsonAsync(
            "/api/sessions/session-1/favorite",
            new { Favorite = false },
            TestContext.Current.CancellationToken);
        unfavoriteResponse.EnsureSuccessStatusCode();

        var repository = factory.Services.GetRequiredService<ISessionOverridesRepository>();
        var saved = await repository.GetOverrideAsync(
            "session-1",
            TestContext.Current.CancellationToken);
        Assert.Null(saved);
    }

    [Fact]
    public async Task SaveOverride_FavoritedSession_PreservesFavoriteState()
    {
        using var factory = new NarniaWebAppFactory();
        using var client = factory.CreateClient();

        var favoriteResponse = await client.PostAsJsonAsync(
            "/api/sessions/session-1/favorite",
            new { Favorite = true },
            TestContext.Current.CancellationToken);
        favoriteResponse.EnsureSuccessStatusCode();

        var overrideResponse = await client.PostAsJsonAsync(
            "/api/sessions/session-1/overrides",
            MakeRequest("ncosentino/needlr"),
            TestContext.Current.CancellationToken);
        overrideResponse.EnsureSuccessStatusCode();

        var repository = factory.Services.GetRequiredService<ISessionOverridesRepository>();
        var saved = await repository.GetOverrideAsync(
            "session-1",
            TestContext.Current.CancellationToken);
        Assert.NotNull(saved);
        Assert.True(saved.IsFavorite);
        Assert.Equal("ncosentino/needlr", saved.Repository);
    }

    [Fact]
    public async Task ArchiveSession_FavoritedSession_PreservesBothStates()
    {
        using var factory = new NarniaWebAppFactory();
        using var client = factory.CreateClient();

        var favoriteResponse = await client.PostAsJsonAsync(
            "/api/sessions/session-1/favorite",
            new { Favorite = true },
            TestContext.Current.CancellationToken);
        favoriteResponse.EnsureSuccessStatusCode();

        var archiveResponse = await client.PostAsJsonAsync(
            "/api/sessions/session-1/archive",
            new { Archived = true },
            TestContext.Current.CancellationToken);
        archiveResponse.EnsureSuccessStatusCode();

        var repository = factory.Services.GetRequiredService<ISessionOverridesRepository>();
        var saved = await repository.GetOverrideAsync(
            "session-1",
            TestContext.Current.CancellationToken);
        Assert.NotNull(saved);
        Assert.True(saved.IsFavorite);
        Assert.True(saved.IsArchived);
    }

    [Fact]
    public async Task ResetMetadata_FavoritedSession_PreservesFavoriteState()
    {
        using var factory = new NarniaWebAppFactory();
        using var client = factory.CreateClient();

        var favoriteResponse = await client.PostAsJsonAsync(
            "/api/sessions/session-1/favorite",
            new { Favorite = true },
            TestContext.Current.CancellationToken);
        favoriteResponse.EnsureSuccessStatusCode();

        var overrideResponse = await client.PostAsJsonAsync(
            "/api/sessions/session-1/overrides",
            MakeRequest("ncosentino/needlr"),
            TestContext.Current.CancellationToken);
        overrideResponse.EnsureSuccessStatusCode();

        var resetResponse = await client.DeleteAsync(
            "/api/sessions/session-1/overrides",
            TestContext.Current.CancellationToken);
        resetResponse.EnsureSuccessStatusCode();

        var repository = factory.Services.GetRequiredService<ISessionOverridesRepository>();
        var saved = await repository.GetOverrideAsync(
            "session-1",
            TestContext.Current.CancellationToken);
        Assert.NotNull(saved);
        Assert.True(saved.IsFavorite);
        Assert.Null(saved.Repository);
    }

    [Fact]
    public async Task FavoriteAndArchive_ConcurrentUpdates_PreserveBothStates()
    {
        using var factory = new NarniaWebAppFactory();
        using var client = factory.CreateClient();

        var favoriteTask = client.PostAsJsonAsync(
            "/api/sessions/session-1/favorite",
            new { Favorite = true },
            TestContext.Current.CancellationToken);
        var archiveTask = client.PostAsJsonAsync(
            "/api/sessions/session-1/archive",
            new { Archived = true },
            TestContext.Current.CancellationToken);

        var responses = await Task.WhenAll(favoriteTask, archiveTask);
        Assert.All(responses, response => response.EnsureSuccessStatusCode());

        var repository = factory.Services.GetRequiredService<ISessionOverridesRepository>();
        var saved = await repository.GetOverrideAsync(
            "session-1",
            TestContext.Current.CancellationToken);
        Assert.NotNull(saved);
        Assert.True(saved.IsFavorite);
        Assert.True(saved.IsArchived);
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
