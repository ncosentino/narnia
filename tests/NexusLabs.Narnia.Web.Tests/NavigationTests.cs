using System.Text.RegularExpressions;

namespace NexusLabs.Narnia.Web.Tests;

public sealed class NavigationTests
{
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
            var icon = Regex.Match(
                anchor.Groups["body"].Value,
                """<span class="nav-icon" aria-hidden="true">(?<value>.*?)</span>""",
                RegexOptions.Singleline);

            Assert.True(icon.Success);
            Assert.False(string.IsNullOrWhiteSpace(icon.Groups["value"].Value));
        }
    }
}
