using NexusLabs.Narnia.Core.Services;

namespace NexusLabs.Narnia.Core.Tests;

public sealed class SessionOperationCoordinatorTests
{
    [Fact]
    public async Task AcquireAsync_SameSession_WaitsForExistingLease()
    {
        var coordinator = new SessionOperationCoordinator();
        var first = await coordinator.AcquireAsync(
            ["session-1"],
            TestContext.Current.CancellationToken);
        var secondTask = coordinator.AcquireAsync(
            ["session-1"],
            TestContext.Current.CancellationToken).AsTask();

        Assert.False(secondTask.IsCompleted);

        await first.DisposeAsync();
        var second = await secondTask.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);
        await second.DisposeAsync();
    }
}
