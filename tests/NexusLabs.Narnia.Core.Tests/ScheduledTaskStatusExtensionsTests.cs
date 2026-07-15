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
    [InlineData(ScheduledTaskHealthKind.Running, false)]
    [InlineData(ScheduledTaskHealthKind.Succeeded, false)]
    public void RequiresAttention_OnlyFlagsFailuresAndDrift(
        ScheduledTaskHealthKind health,
        bool expected)
    {
        Assert.Equal(expected, health.RequiresAttention());
    }

    private static ScheduledTaskStatus Status(ScheduledTaskState state, int? lastResult) =>
        new(@"\Narnia\", "Example", state, null, lastResult, null, null);
}
