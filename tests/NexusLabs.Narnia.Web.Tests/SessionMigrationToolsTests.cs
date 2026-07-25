using System.Text.Json;
using NexusLabs.Narnia.Core.Models;
using NexusLabs.Narnia.Core.Services;
using NexusLabs.Narnia.Web.Mcp;

namespace NexusLabs.Narnia.Web.Tests;

public sealed class SessionMigrationToolsTests
{
    private const string SourceId = "33333333-3333-4333-8333-333333333333";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task MigrateBrokenSessionAsync_RequiresConfirmation()
    {
        var service = new Mock<ISessionMigrationService>();
        var tools = new SessionMigrationTools(service.Object);

        var json = await tools.MigrateBrokenSessionAsync(SourceId, false, Ct);

        Assert.Contains("not explicitly confirmed", json, StringComparison.Ordinal);
        service.Verify(candidate => candidate.MigrateAsync(
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetSessionRecoveryPacketAsync_ReturnsBoundedChunk()
    {
        var service = new Mock<ISessionMigrationService>();
        service
            .Setup(candidate => candidate.ReadPacketAsync(
                SourceId,
                0,
                50_000,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SessionRecoveryPacketChunk("history", 0, null, 7));
        var tools = new SessionMigrationTools(service.Object);

        var json = await tools.GetSessionRecoveryPacketAsync(
            SourceId,
            0,
            100_000,
            Ct);
        using var document = JsonDocument.Parse(json);

        Assert.Equal("history", document.RootElement.GetProperty("content").GetString());
        Assert.Equal(7, document.RootElement.GetProperty("totalCharacters").GetInt32());
    }
}
