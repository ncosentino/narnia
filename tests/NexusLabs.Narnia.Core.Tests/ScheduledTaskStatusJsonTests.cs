using NexusLabs.Narnia.Core.Models;
using NexusLabs.Narnia.Core.Services;

namespace NexusLabs.Narnia.Core.Tests;

public sealed class ScheduledTaskStatusJsonTests
{
    [Fact]
    public void ParseLine_FullObject_MapsAllFields()
    {
        const string json =
            """{"folder":"\\Narnia\\","name":"Sample Daily","state":"Ready","lastRunTime":"2026-06-28T05:00:01.0000000-07:00","lastResult":0,"nextRunTime":"2026-06-29T05:00:00.0000000-07:00","action":"powershell.exe -File run.ps1"}""";

        var status = ScheduledTaskStatusJson.ParseLine(json);

        Assert.NotNull(status);
        Assert.Equal(@"\Narnia\", status!.TaskFolder);
        Assert.Equal("Sample Daily", status.TaskName);
        Assert.Equal(ScheduledTaskState.Ready, status.State);
        Assert.Equal(0, status.LastResult);
        Assert.NotNull(status.LastRunTime);
        Assert.NotNull(status.NextRunTime);
        Assert.Equal("powershell.exe -File run.ps1", status.ActionSummary);
    }

    [Fact]
    public void ParseLine_NeverRun_HasNullLastRunAndHasNotRunYetCode()
    {
        const string json =
            """{"folder":"\\Narnia\\","name":"Monthly","state":"Ready","lastRunTime":null,"lastResult":267011,"nextRunTime":"2026-07-01T06:00:00.0000000-07:00","action":"x"}""";

        var status = ScheduledTaskStatusJson.ParseLine(json);

        Assert.NotNull(status);
        Assert.Null(status!.LastRunTime);
        Assert.Equal(267011, status.LastResult);
        Assert.NotNull(status.NextRunTime);
    }

    [Fact]
    public void ParseLine_FailingResult_IsPreserved()
    {
        const string json =
            """{"folder":"\\Narnia\\","name":"Failing","state":"Ready","lastRunTime":"2026-06-26T05:30:00-07:00","lastResult":1,"nextRunTime":null,"action":"x"}""";

        var status = ScheduledTaskStatusJson.ParseLine(json);

        Assert.Equal(1, status!.LastResult);
        Assert.Null(status.NextRunTime);
    }

    [Theory]
    [InlineData("disabled", ScheduledTaskState.Disabled)]
    [InlineData("Running", ScheduledTaskState.Running)]
    [InlineData("Queued", ScheduledTaskState.Queued)]
    [InlineData("somethingElse", ScheduledTaskState.Unknown)]
    public void ParseLine_StateMapping_IsCaseInsensitiveWithUnknownFallback(string raw, ScheduledTaskState expected)
    {
        var json = $$"""{"folder":"\\Narnia\\","name":"n","state":"{{raw}}","lastResult":0}""";

        var status = ScheduledTaskStatusJson.ParseLine(json);

        Assert.Equal(expected, status!.State);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("[1,2,3]")]
    [InlineData("{ broken")]
    public void ParseLine_BlankOrMalformed_ReturnsNull(string? line)
    {
        Assert.Null(ScheduledTaskStatusJson.ParseLine(line));
    }

    [Fact]
    public void ParseLines_MixedOutput_SkipsBlankAndNonObjectLines()
    {
        var output = string.Join('\n',
            "",
            """{"folder":"\\Narnia\\","name":"A","state":"Ready","lastResult":0}""",
            "   ",
            "garbage",
            """{"folder":"\\Narnia\\","name":"B","state":"Disabled","lastResult":0}""");

        var statuses = ScheduledTaskStatusJson.ParseLines(output);

        Assert.Equal(["A", "B"], statuses.Select(s => s.TaskName));
        Assert.Equal(ScheduledTaskState.Disabled, statuses[1].State);
    }

    [Fact]
    public void ParseLines_EmptyOutput_ReturnsEmpty()
    {
        Assert.Empty(ScheduledTaskStatusJson.ParseLines(""));
        Assert.Empty(ScheduledTaskStatusJson.ParseLines(null));
    }
}
