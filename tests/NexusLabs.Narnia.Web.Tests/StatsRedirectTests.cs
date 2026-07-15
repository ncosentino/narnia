using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace NexusLabs.Narnia.Web.Tests;

public sealed class StatsRedirectTests
{
    [Theory]
    [InlineData("/activity", "/stats")]
    [InlineData("/analytics/activity?days=30", "/stats?days=30")]
    [InlineData("/hot-files", "/stats")]
    [InlineData("/analytics/hot-files", "/stats")]
    public async Task RetiredAnalyticsRoute_RedirectsToStats(string source, string target)
    {
        using var factory = new NarniaWebAppFactory();
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        var response = await client.GetAsync(source, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = Assert.IsType<Uri>(response.Headers.Location);
        Assert.Equal(target, location.IsAbsoluteUri ? location.PathAndQuery : location.OriginalString);
    }
}
