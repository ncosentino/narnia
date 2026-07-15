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
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
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
                new ActivityTimelineDay(today.AddDays(-1), 1, 2, 1, 0),
                new ActivityTimelineDay(today, 1, 1, 2, 3),
            ]);
        factory.SessionRepository
            .Setup(repository => repository.GetHotFilesAsync(25, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new HotFile("src/Program.cs", 4, "edit"),
                new HotFile("docs/README.md", 2, "create"),
            ]);
        factory.SessionRepository
            .Setup(repository => repository.GetSessionActivitySourcesAsync(
                It.IsAny<DateOnly>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new SessionActivitySource(
                    SessionActivitySourceKind.WorkingDirectory,
                    @"C:\Temp\bg-eval-judge\*",
                    null,
                    @"C:\Temp\bg-eval-judge",
                    true,
                    null,
                    1517),
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

        Assert.Equal([1, 2], ReadValues(datasets["Total Raw Sessions"]));
        Assert.Equal([2, 3], ReadValues(datasets["Total Turns"]));
        Assert.Equal([1, 3], ReadValues(datasets["Total Files Touched"]));
        Assert.Equal([0, 3], ReadValues(datasets["Total Checkpoints"]));
        Assert.Equal("y", datasets["Total Raw Sessions"].GetProperty("yAxisID").GetString());
        Assert.Equal("work", datasets["Total Turns"].GetProperty("yAxisID").GetString());

        using var activityChart = ParseChart(html, "stats-activity-chart");
        var activityDataset = activityChart.RootElement
            .GetProperty("data")
            .GetProperty("datasets")[0];
        Assert.Equal([1, 1], ReadValues(activityDataset));

        using var hotFilesChart = ParseChart(html, "stats-hot-files-chart");
        var hotFilesDataset = hotFilesChart.RootElement
            .GetProperty("data")
            .GetProperty("datasets")[0];
        Assert.Equal([4, 2], ReadValues(hotFilesDataset));
        Assert.Contains("data-resizable-table=\"stats-hot-files\"", html, StringComparison.Ordinal);
        Assert.Contains("/files?path=src%2FProgram.cs", html, StringComparison.Ordinal);
        Assert.Contains("data-chart-href-template=", html, StringComparison.Ordinal);
        Assert.Contains(@"C:\Temp\bg-eval-judge\*", html, StringComparison.Ordinal);
        Assert.Contains("1517", html, StringComparison.Ordinal);
        Assert.Contains("activitySourceKind=WorkingDirectory", html, StringComparison.Ordinal);
        Assert.Contains("activitySource=C%3A%5CTemp%5Cbg-eval-judge", html, StringComparison.Ordinal);
        Assert.Contains("activityGeneratedChildren=true", html, StringComparison.Ordinal);
        Assert.Contains("activityHostTypeMissing=true", html, StringComparison.Ordinal);
        Assert.Contains("showArchived=true", html, StringComparison.Ordinal);
    }

    private static JsonDocument ParseChart(string html, string chartId)
    {
        var match = Regex.Match(
            html,
            $"""<script type="application/json"[^>]*data-chart-id="{chartId}"[^>]*>\s*(?<json>.*?)\s*</script>""",
            RegexOptions.Singleline);
        Assert.True(match.Success);
        return JsonDocument.Parse(match.Groups["json"].Value);
    }

    private static int[] ReadValues(JsonElement dataset) =>
        [.. dataset.GetProperty("data").EnumerateArray().Select(value => value.GetInt32())];
}
