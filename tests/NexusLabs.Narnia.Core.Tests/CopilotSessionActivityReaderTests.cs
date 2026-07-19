using NexusLabs.Narnia.Core.Repositories;
using NexusLabs.Narnia.Core.Services;

namespace NexusLabs.Narnia.Core.Tests;

public sealed class CopilotSessionActivityReaderTests
{
    [Fact]
    public void GetActiveSessionIds_UsesOnlyVerifiedCopilotProcessIdsAndKeepsSharedPidSessions()
    {
        var processes = new Mock<ICopilotProcessProvider>();
        processes.Setup(provider => provider.GetProcessIds()).Returns([100, 200]);
        var locks = new Mock<ICopilotSessionLockReader>();
        locks.Setup(reader => reader.GetSessionIds(100)).Returns(["main", "subagent"]);
        locks.Setup(reader => reader.GetSessionIds(200)).Returns(["other"]);
        var reader = new CopilotSessionActivityReader(processes.Object, locks.Object);

        var active = reader.GetActiveSessionIds();

        Assert.Equal(3, active.Count);
        Assert.Contains("main", active);
        Assert.Contains("subagent", active);
        Assert.Contains("other", active);
    }
}
