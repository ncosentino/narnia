using NexusLabs.Narnia.Core.Models;
using NexusLabs.Narnia.Core.Services;

namespace NexusLabs.Narnia.Core.Tests;

public sealed class SessionTerminationParserTests
{
    [Fact]
    public void Classify_SessionThatFinishedItsWork_IsCompleted()
    {
        string[] lines =
        [
            Event("tool.execution_complete"),
            Event("assistant.message"),
            Event("assistant.turn_end"),
            Event("session.usage_checkpoint"),
            Event("session.shutdown"),
        ];

        var termination = SessionTerminationParser.Classify(lines);

        Assert.Equal(ScheduledRunCompletion.Completed, termination.Completion);
        Assert.Null(termination.AbortReason);
    }

    [Fact]
    public void Classify_SessionKilledMidToolCall_IsInterruptedWithItsReason()
    {
        // The shutdown after an abort is still recorded as routine and the process still exits 0,
        // so the abort event is the only thing that distinguishes this from a healthy run.
        string[] lines =
        [
            Event("assistant.turn_start"),
            Event("tool.execution_start"),
            AbortEvent("user_initiated"),
            Event("session.usage_checkpoint"),
            Event("session.shutdown"),
        ];

        var termination = SessionTerminationParser.Classify(lines);

        Assert.Equal(ScheduledRunCompletion.Interrupted, termination.Completion);
        Assert.Equal("user_initiated", termination.AbortReason);
    }

    [Fact]
    public void Classify_AbortFollowedByMoreWork_IsCompleted()
    {
        // An interactive cancel that the session recovers from must not be reported as the thing
        // that ended the run.
        string[] lines =
        [
            AbortEvent("user_initiated"),
            Event("user.message"),
            Event("assistant.turn_start"),
            Event("assistant.turn_end"),
            Event("session.shutdown"),
        ];

        var termination = SessionTerminationParser.Classify(lines);

        Assert.Equal(ScheduledRunCompletion.Completed, termination.Completion);
        Assert.Null(termination.AbortReason);
    }

    [Fact]
    public void Classify_ToolCompletingAfterAnAbort_StaysInterrupted()
    {
        // A tool cancelled by the abort can still report back afterwards; that is not the session
        // resuming work.
        string[] lines =
        [
            Event("tool.execution_start"),
            AbortEvent("user_initiated"),
            Event("tool.execution_complete"),
            Event("session.shutdown"),
        ];

        Assert.Equal(
            ScheduledRunCompletion.Interrupted,
            SessionTerminationParser.Classify(lines).Completion);
    }

    [Fact]
    public void Classify_AbortWithoutARecordedReason_IsStillInterrupted()
    {
        string[] lines = [Event("assistant.turn_start"), """{"type":"abort"}""", Event("session.shutdown")];

        var termination = SessionTerminationParser.Classify(lines);

        Assert.Equal(ScheduledRunCompletion.Interrupted, termination.Completion);
        Assert.Null(termination.AbortReason);
    }

    [Fact]
    public void Classify_NoReadableEvents_IsUnknownRatherThanCompleted()
    {
        // A tail that cut every line in half must not be read as a clean finish.
        string[] lines = ["", "   ", "{\"type\":", "not json at all"];

        Assert.Equal(
            ScheduledRunCompletion.Unknown,
            SessionTerminationParser.Classify(lines).Completion);
    }

    [Fact]
    public void Classify_SkipsUnparseableLinesWithoutLosingTheAbort()
    {
        string[] lines =
        [
            "d3b07384d113edec49eaa6238ad5ff00\"}",
            Event("tool.execution_start"),
            AbortEvent("user_initiated"),
            Event("session.shutdown"),
        ];

        Assert.Equal(
            ScheduledRunCompletion.Interrupted,
            SessionTerminationParser.Classify(lines).Completion);
    }

    [Fact]
    public void Classify_NonObjectJsonLine_IsIgnored()
    {
        string[] lines = ["[1,2,3]", "\"a string\"", "42"];

        Assert.Equal(
            ScheduledRunCompletion.Unknown,
            SessionTerminationParser.Classify(lines).Completion);
    }

    private static string Event(string type) =>
        $$"""{"type":"{{type}}","timestamp":"2026-08-08T07:43:09.931Z"}""";

    private static string AbortEvent(string reason) =>
        $$"""{"type":"abort","data":{"reason":"{{reason}}"},"timestamp":"2026-08-08T07:43:09.931Z"}""";
}
