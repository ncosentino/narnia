using System.Text.Json;
using System.Text.RegularExpressions;
using NexusLabs.Narnia.Core.Models;

namespace NexusLabs.Narnia.Web.Tests;

public sealed class StatsPageTests
{
    [Fact]
    public async Task GrowthChart_RendersCumulativeActivitySeries()
    {
        using var factory = new NarniaWebAppFactory();
        factory.SessionRepository
            .Setup(repository => repository.GetGlobalStatsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GlobalStats(2, 3, 1.5, 3, "owner/repository", "2026-07-01"));
        factory.SessionRepository
            .Setup(repository => repository.GetRepositoryStatsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<RepositoryStats>());
        factory.SessionRepository
            .Setup(repository => repository.GetSessionInsightsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SessionInsights(1, 1, 3, 1, 2, 0, 2));
        factory.SessionRepository
            .Setup(repository => repository.GetActivityPatternsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ActivityPatterns(
                Array.Empty<HourActivity>(),
                Array.Empty<DayOfWeekActivity>(),
                1,
                1));
        factory.SessionRepository
            .Setup(repository => repository.GetActivityTimelineAsync(3650, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new ActivityTimelineDay(new DateOnly(2026, 7, 1), 1, 2, 1, 0),
                new ActivityTimelineDay(new DateOnly(2026, 7, 2), 1, 1, 2, 3),
            ]);

        var client = factory.CreateClient();
        var html = await client.GetStringAsync("/stats", TestContext.Current.CancellationToken);
        var chartMatch = Regex.Match(
            html,
            """<script type="application/json" data-chart-id="stats-growth-chart">\s*(?<json>.*?)\s*</script>""",
            RegexOptions.Singleline);

        Assert.True(chartMatch.Success);
        using var chart = JsonDocument.Parse(chartMatch.Groups["json"].Value);
        var datasets = chart.RootElement
            .GetProperty("data")
            .GetProperty("datasets")
            .EnumerateArray()
            .ToDictionary(
                dataset => dataset.GetProperty("label").GetString()!,
                StringComparer.Ordinal);

        Assert.Equal([1, 2], ReadValues(datasets["Total Sessions"]));
        Assert.Equal([2, 3], ReadValues(datasets["Total Turns"]));
        Assert.Equal([1, 3], ReadValues(datasets["Total Files Touched"]));
        Assert.Equal([0, 3], ReadValues(datasets["Total Checkpoints"]));
        Assert.Equal("y", datasets["Total Sessions"].GetProperty("yAxisID").GetString());
        Assert.Equal("work", datasets["Total Turns"].GetProperty("yAxisID").GetString());
    }

    private static int[] ReadValues(JsonElement dataset) =>
        [.. dataset.GetProperty("data").EnumerateArray().Select(value => value.GetInt32())];
}
