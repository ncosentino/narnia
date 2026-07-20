using Microsoft.Extensions.Logging.Abstractions;
using NexusLabs.Narnia.Core.Configuration;
using NexusLabs.Narnia.Core.Repositories;
using NexusLabs.Narnia.Core.Services;
using NexusLabs.Narnia.Web;

namespace NexusLabs.Narnia.Web.Tests;

public sealed class CopilotSdkSessionManagerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task DeleteSessionsAsync_MismatchedCopilotPaths_ReturnsExplicitFailure()
    {
        var settings = new Mock<INarniaSettingsRepository>();
        var manager = new CopilotSdkSessionManager(
            settings.Object,
            new NarniaOptions
            {
                SessionStatePath = @"C:\copilot-a\session-state",
                DatabasePath = @"C:\copilot-b\session-store.db",
            },
            Mock.Of<IPowerShellHostResolver>(),
            NullLogger<CopilotSdkSessionManager>.Instance);

        var result = await manager.DeleteSessionsAsync(["session-1"], Ct);

        var failure = Assert.Single(result);
        Assert.False(failure.Deleted);
        Assert.Contains("same Copilot home", failure.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeleteSessionsAsync_InvalidConfiguredCommand_ReturnsExplicitFailure()
    {
        var settings = new Mock<INarniaSettingsRepository>();
        settings
            .Setup(repository => repository.GetAsync(
                "copilot_command",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(" ");
        var manager = new CopilotSdkSessionManager(
            settings.Object,
            new NarniaOptions
            {
                SessionStatePath = @"C:\copilot\session-state",
                DatabasePath = @"C:\copilot\session-store.db",
            },
            Mock.Of<IPowerShellHostResolver>(),
            NullLogger<CopilotSdkSessionManager>.Instance);

        var result = await manager.DeleteSessionsAsync(["session-1"], Ct);

        var failure = Assert.Single(result);
        Assert.False(failure.Deleted);
        Assert.Contains("blank", failure.Error, StringComparison.Ordinal);
    }
}
