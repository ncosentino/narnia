using NexusLabs.Narnia.Core.Models;

namespace NexusLabs.Narnia.Core.Tests;

public sealed class SessionSummarySortingTests
{
    private static SessionSummary Make(
        string id,
        string? summary = null,
        string? repository = null,
        string? cwd = null,
        int turnCount = 0,
        int checkpointCount = 0,
        DateTimeOffset updatedAt = default) =>
        new(id, cwd, repository, null, summary, default, updatedAt, turnCount, checkpointCount);

    [Theory]
    [InlineData("summary", SessionSortColumn.Summary)]
    [InlineData("Summary", SessionSortColumn.Summary)]
    [InlineData("repository", SessionSortColumn.Repository)]
    [InlineData("directory", SessionSortColumn.Directory)]
    [InlineData("turns", SessionSortColumn.Turns)]
    [InlineData("checkpoints", SessionSortColumn.Checkpoints)]
    [InlineData("updated", SessionSortColumn.Updated)]
    public void ParseColumn_KnownValues_ReturnsColumn(string value, SessionSortColumn expected)
    {
        Assert.Equal(expected, SessionSummarySorting.ParseColumn(value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unknown")]
    public void ParseColumn_UnknownValues_ReturnsNull(string? value)
    {
        Assert.Null(SessionSummarySorting.ParseColumn(value));
    }

    [Theory]
    [InlineData("asc", SessionSortDirection.Ascending)]
    [InlineData("ASC", SessionSortDirection.Ascending)]
    [InlineData("desc", SessionSortDirection.Descending)]
    [InlineData(null, SessionSortDirection.Descending)]
    [InlineData("anything", SessionSortDirection.Descending)]
    public void ParseDirection_ReturnsExpected(string? value, SessionSortDirection expected)
    {
        Assert.Equal(expected, SessionSummarySorting.ParseDirection(value));
    }

    [Fact]
    public void Sort_BySummary_Ascending_OrdersCaseInsensitively()
    {
        var sessions = new[]
        {
            Make("1", summary: "banana"),
            Make("2", summary: "Apple"),
            Make("3", summary: "cherry"),
        };

        var sorted = SessionSummarySorting.Sort(sessions, SessionSortColumn.Summary, SessionSortDirection.Ascending);

        Assert.Equal(new[] { "2", "1", "3" }, sorted.Select(s => s.Id));
    }

    [Fact]
    public void Sort_ByTurns_Descending_OrdersByCount()
    {
        var sessions = new[]
        {
            Make("1", turnCount: 5),
            Make("2", turnCount: 20),
            Make("3", turnCount: 10),
        };

        var sorted = SessionSummarySorting.Sort(sessions, SessionSortColumn.Turns, SessionSortDirection.Descending);

        Assert.Equal(new[] { "2", "3", "1" }, sorted.Select(s => s.Id));
    }

    [Fact]
    public void Sort_ByUpdated_Ascending_OrdersByTimestamp()
    {
        var sessions = new[]
        {
            Make("1", updatedAt: new DateTimeOffset(2024, 1, 3, 0, 0, 0, TimeSpan.Zero)),
            Make("2", updatedAt: new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            Make("3", updatedAt: new DateTimeOffset(2024, 1, 2, 0, 0, 0, TimeSpan.Zero)),
        };

        var sorted = SessionSummarySorting.Sort(sessions, SessionSortColumn.Updated, SessionSortDirection.Ascending);

        Assert.Equal(new[] { "2", "3", "1" }, sorted.Select(s => s.Id));
    }

    [Fact]
    public void Sort_ByRepository_Ascending_PlacesNullsLast()
    {
        var sessions = new[]
        {
            Make("1", repository: "owner/zebra"),
            Make("2", repository: null),
            Make("3", repository: "owner/apple"),
        };

        var sorted = SessionSummarySorting.Sort(sessions, SessionSortColumn.Repository, SessionSortDirection.Ascending);

        Assert.Equal(new[] { "3", "1", "2" }, sorted.Select(s => s.Id));
    }

    [Fact]
    public void Sort_ByRepository_Descending_PlacesNullsLast()
    {
        var sessions = new[]
        {
            Make("1", repository: "owner/zebra"),
            Make("2", repository: null),
            Make("3", repository: "owner/apple"),
        };

        var sorted = SessionSummarySorting.Sort(sessions, SessionSortColumn.Repository, SessionSortDirection.Descending);

        Assert.Equal(new[] { "1", "3", "2" }, sorted.Select(s => s.Id));
    }

    [Fact]
    public void Sort_ByRepository_UsesNewestUpdatedSessionAsTieBreaker()
    {
        var sessions = new[]
        {
            Make("older", repository: "owner/repo", updatedAt: new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            Make("newer", repository: "owner/repo", updatedAt: new DateTimeOffset(2024, 1, 2, 0, 0, 0, TimeSpan.Zero)),
        };

        var sorted = SessionSummarySorting.Sort(sessions, SessionSortColumn.Repository, SessionSortDirection.Ascending);

        Assert.Equal(new[] { "newer", "older" }, sorted.Select(s => s.Id));
    }

    [Fact]
    public void Sort_DoesNotMutateInput()
    {
        var sessions = new[]
        {
            Make("1", checkpointCount: 3),
            Make("2", checkpointCount: 1),
        };

        _ = SessionSummarySorting.Sort(sessions, SessionSortColumn.Checkpoints, SessionSortDirection.Ascending);

        Assert.Equal(new[] { "1", "2" }, sessions.Select(s => s.Id));
    }
}
