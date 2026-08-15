using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace NexusLabs.Narnia.Web.Tests;

public sealed class RuntimeRedirectTests
{
    [Theory]
    [InlineData("/runtime", "/runtime/windows")]
    [InlineData("/windows", "/runtime/windows")]
    [InlineData("/processes", "/runtime/processes")]
    [InlineData("/processes?q=1234", "/runtime/processes?q=1234")]
    [InlineData("/session-groups", "/collections?from=session-groups")]
    [InlineData("/groups", "/collections?from=session-groups")]
    public async Task LegacyRoute_RedirectsToCanonicalArea(string source, string target)
    {
        using var factory = new NarniaWebAppFactory();
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        using var response = await client.GetAsync(
            source,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = Assert.IsType<Uri>(response.Headers.Location);
        Assert.Equal(
            target,
            location.IsAbsoluteUri ? location.PathAndQuery : location.OriginalString);
    }
}
