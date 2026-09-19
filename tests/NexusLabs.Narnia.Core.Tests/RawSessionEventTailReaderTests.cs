using System.IO.Abstractions.TestingHelpers;
using NexusLabs.Narnia.Core.Configuration;
using NexusLabs.Narnia.Core.Models;
using NexusLabs.Narnia.Core.Services;

namespace NexusLabs.Narnia.Core.Tests;

public sealed class RawSessionEventTailReaderTests
{
    private const string SessionId = "55555555-5555-4555-8555-555555555555";
    private const string Root = @"C:\copilot\session-state";

    [Fact]
    public async Task ReadAsync_RetainsNewestUserMessageAndMarksStaleIndex()
    {
        var path = $@"{Root}\{SessionId}\events.jsonl";
        var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [path] = new(string.Join('\n',
                "{\"type\":\"session.start\",\"timestamp\":\"2026-07-24T12:00:00Z\"}",
                "{\"type\":\"user.message\",\"timestamp\":\"2026-07-24T12:05:00Z\",\"data\":{\"content\":\"Continue the deployment\"}}")),
        });
        var reader = new RawSessionEventTailReader(
            new NarniaOptions { SessionStatePath = Root },
            fileSystem);

        var result = await reader.ReadAsync(new Session(
            SessionId,
            null,
            null,
            null,
            null,
            null,
            DateTimeOffset.Parse("2026-07-24T11:00:00Z"),
            DateTimeOffset.Parse("2026-07-24T12:01:00Z")),
            TestContext.Current.CancellationToken);

        Assert.True(result.IndexMayBeStale);
        Assert.Contains(result.Events, item => item.UserMessage == "Continue the deployment");
        Assert.Equal(DateTimeOffset.Parse("2026-07-24T12:05:00Z"), result.LatestTimestamp);
    }

    [Fact]
    public async Task ReadAsync_IgnoresMalformedLinesWithoutFailing()
    {
        var path = $@"{Root}\{SessionId}\events.jsonl";
        var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [path] = new("not-json\n{\"type\":\"user.message\",\"data\":{\"text\":\"latest\"}}\n"),
        });
        var reader = new RawSessionEventTailReader(
            new NarniaOptions { SessionStatePath = Root },
            fileSystem);

        var result = await reader.ReadAsync(new Session(
            SessionId, null, null, null, null,
            null,
            DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow),
            TestContext.Current.CancellationToken);

        Assert.True(result.Truncated);
        Assert.Contains(result.Events, item => item.UserMessage == "latest");
    }
}
