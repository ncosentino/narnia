using System.Text.RegularExpressions;

namespace NexusLabs.Narnia.Web.Tests;

public sealed class NavigationTests
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    [Fact]
    public async Task TopNavigation_EveryPageLinkHasAnIcon()
    {
        using var factory = new NarniaWebAppFactory();
        var client = factory.CreateClient();

        var html = await client.GetStringAsync("/commits", TestContext.Current.CancellationToken);
        var navMatch = Regex.Match(
            html,
            """<header class="header">.*?<nav>(?<content>.*?)</nav>""",
            RegexOptions.Singleline);
        Assert.True(navMatch.Success);

        var anchors = Regex.Matches(
            navMatch.Groups["content"].Value,
            """<a\b[^>]*>(?<body>.*?)</a>""",
            RegexOptions.Singleline);
        var expectedLabels = new[]
        {
            "Narnia",
            "Sessions",
            "Favorites",
            "Collections",
            "Windows",
            "Session Groups",
            "Schedules",
            "Stats",
            "Remote Repositories",
            "Tags",
            "Files",
            "Commits",
            "Settings",
            "Docs",
        };

        Assert.Equal(expectedLabels.Length, anchors.Count);
        foreach (var label in expectedLabels)
        {
            var anchor = Assert.Single(
                anchors.Cast<System.Text.RegularExpressions.Match>(),
                match => match.Groups["body"].Value.Contains(
                    $"<span>{label}</span>",
                    StringComparison.Ordinal));
            if (label == "Narnia")
            {
                var image = Regex.Match(
                    anchor.Groups["body"].Value,
                    """<img\b(?=[^>]*class="nav-icon nav-icon-image")(?=[^>]*src="/narnia-logo.png")[^>]*>""");

                Assert.True(image.Success);
                continue;
            }

            var icon = Regex.Match(
                anchor.Groups["body"].Value,
                """<span class="nav-icon" aria-hidden="true">(?<value>.*?)</span>""",
                RegexOptions.Singleline);

            Assert.True(icon.Success);
            Assert.False(string.IsNullOrWhiteSpace(icon.Groups["value"].Value));
        }
    }

    [Fact]
    public async Task RuntimeBranding_ReferencesAndServesSharedLogoAssets()
    {
        using var factory = new NarniaWebAppFactory();
        var client = factory.CreateClient();

        var html = await client.GetStringAsync("/", TestContext.Current.CancellationToken);

        Assert.Matches(
            """<link rel="icon" type="image/png" sizes="64x64" href="/favicon.png"\s*/?>""",
            html);
        await AssertPngAsync(client, "/favicon.png");
        await AssertPngAsync(client, "/narnia-logo.png");
    }

    private static async Task AssertPngAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
        Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
        var content = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.True(content.AsSpan().StartsWith(PngSignature));
    }
}
