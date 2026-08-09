using NexusLabs.Narnia.Core.Models;

namespace NexusLabs.Narnia.Core.Tests;

public sealed class ScheduledTaskStatusExtensionsTests
{
    [Fact]
    public void GetHealthKind_MissingStatus_IsDrift()
    {
        ScheduledTaskStatus? status = null;

        Assert.Equal(ScheduledTaskHealthKind.Drift, status.GetHealthKind());
    }

    [Fact]
    public void GetHealthKind_RunningState_WinsOverRetainedFailureResult()
    {
        var status = Status(ScheduledTaskState.Running, lastResult: 1);

        Assert.Equal(ScheduledTaskHealthKind.Running, status.GetHealthKind());
    }

    [Fact]
    public void GetHealthKind_DisabledState_WinsOverRetainedSuccessResult()
    {
        var status = Status(ScheduledTaskState.Disabled, lastResult: 0);

        Assert.Equal(ScheduledTaskHealthKind.Disabled, status.GetHealthKind());
    }

    [Theory]
    [InlineData(null, ScheduledTaskHealthKind.NeverRun)]
    [InlineData(0, ScheduledTaskHealthKind.Succeeded)]
    [InlineData(267010, ScheduledTaskHealthKind.Disabled)]
    [InlineData(267015, ScheduledTaskHealthKind.NoValidTriggers)]
    [InlineData(1, ScheduledTaskHealthKind.Failed)]
    public void GetHealthKind_ClassifiesLastResult(
        int? lastResult,
        ScheduledTaskHealthKind expected)
    {
        Assert.Equal(expected, Status(ScheduledTaskState.Ready, lastResult).GetHealthKind());
    }

    [Theory]
    [InlineData(ScheduledTaskHealthKind.Drift, true)]
    [InlineData(ScheduledTaskHealthKind.Failed, true)]
    [InlineData(ScheduledTaskHealthKind.Interrupted, true)]
    [InlineData(ScheduledTaskHealthKind.Running, false)]
    [InlineData(ScheduledTaskHealthKind.Succeeded, false)]
    public void RequiresAttention_OnlyFlagsFailuresAndDrift(
        ScheduledTaskHealthKind health,
        bool expected)
    {
        Assert.Equal(expected, health.RequiresAttention());
    }

    [Fact]
    public void GetHealthKind_SuccessfulExitCodeWithAnInterruptedSession_IsInterrupted()
    {
        // The Copilot CLI shuts down gracefully when it is interrupted, so the scheduler records a
        // successful exit for a run that never finished its work.
        var status = Status(ScheduledTaskState.Ready, lastResult: 0);

        var health = status.GetHealthKind(Interrupted());

        Assert.Equal(ScheduledTaskHealthKind.Interrupted, health);
        Assert.True(health.RequiresAttention());
    }

    [Fact]
    public void GetHealthKind_SuccessfulExitCodeWithACompletedSession_StaysSucceeded()
    {
        var status = Status(ScheduledTaskState.Ready, lastResult: 0);

        Assert.Equal(
            ScheduledTaskHealthKind.Succeeded,
            status.GetHealthKind(new ScheduledRunOutcome(ScheduledRunCompletion.Completed, "s", null)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(ScheduledRunCompletion.Unknown)]
    public void GetHealthKind_WithoutEvidenceOfAnInterruption_StaysSucceeded(
        ScheduledRunCompletion? completion)
    {
        // An unreadable or absent run must never be downgraded: a false alarm on a healthy job
        // hides the real ones.
        var outcome = completion is null
            ? null
            : new ScheduledRunOutcome(completion.Value, null, null);

        Assert.Equal(
            ScheduledTaskHealthKind.Succeeded,
            Status(ScheduledTaskState.Ready, lastResult: 0).GetHealthKind(outcome));
    }

    [Fact]
    public void GetHealthKind_RunningTask_IsNotDowngradedByThePreviousRunsAbort()
    {
        // LastResult still describes the previous run while a new one is executing.
        var status = Status(ScheduledTaskState.Running, lastResult: 0);

        Assert.Equal(ScheduledTaskHealthKind.Running, status.GetHealthKind(Interrupted()));
    }

    [Fact]
    public void GetHealthKind_FailedTask_KeepsItsFailureRatherThanBecomingInterrupted()
    {
        var status = Status(ScheduledTaskState.Ready, lastResult: 1);

        Assert.Equal(ScheduledTaskHealthKind.Failed, status.GetHealthKind(Interrupted()));
    }

    [Fact]
    public void GetHealthKind_MissingTaskWithAnInterruptedRun_StaysDrift()
    {
        ScheduledTaskStatus? status = null;

        Assert.Equal(ScheduledTaskHealthKind.Drift, status.GetHealthKind(Interrupted()));
    }

    private static ScheduledRunOutcome Interrupted() =>
        new(ScheduledRunCompletion.Interrupted, "1b7cf2d0-9d2b-4f0d-9d8f-6b0f1e2a3c4d", "user_initiated");

    private static ScheduledTaskStatus Status(ScheduledTaskState state, int? lastResult) =>
        new(@"\Narnia\", "Example", state, null, lastResult, null, null);
}
