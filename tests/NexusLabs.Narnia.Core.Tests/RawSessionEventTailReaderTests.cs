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
    public async Task ReadAsync_FindsDirectMessageMoreThanOneMegabyteBeforeEnd()
    {
        var filler = string.Join('\n', Enumerable.Repeat(
            "{\"type\":\"tool.execution_complete\",\"data\":{\"content\":\"" + new string('x', 4096) + "\"}}",
            300));
        var result = await ReadAsync(
            "{\"type\":\"user.message\",\"timestamp\":\"2026-07-24T12:05:00Z\",\"data\":{\"content\":\"Continue the deployment\"}}\n" + filler,
            []);

        Assert.True(result.IndexMayBeStale);
        Assert.Contains(result.DirectUserMessages, item => item.Message == "Continue the deployment");
        Assert.Equal(DateTimeOffset.Parse("2026-07-24T12:05:00Z"), result.LatestMessageTimestamp);
    }

    [Fact]
    public async Task ReadAsync_ClassifiesDirectAndSourcedMessagesSeparately()
    {
        var result = await ReadAsync(string.Join('\n',
            "{\"type\":\"user.message\",\"timestamp\":\"2026-07-24T12:05:00Z\",\"data\":{\"content\":\"Human direction\"}}",
            "{\"type\":\"user.message\",\"timestamp\":\"2026-07-24T12:06:00Z\",\"data\":{\"content\":\"Agent steering\",\"source\":\"task-agent\"}}"),
            []);

        var direct = Assert.Single(result.DirectUserMessages);
        Assert.Equal("Human direction", direct.Message);
        Assert.Null(direct.Source);
        var steering = Assert.Single(result.SteeringMessages);
        Assert.Equal("Agent steering", steering.Message);
        Assert.Equal("task-agent", steering.Source);
    }

    [Fact]
    public async Task ReadAsync_MatchingIndexedMessageDoesNotMarkStale()
    {
        var indexed = new Turn(1, SessionId, 0, "  Continue   the deployment  ", null, DateTimeOffset.UtcNow);
        var result = await ReadAsync(
            "{\"type\":\"user.message\",\"data\":{\"content\":\"Continue the deployment\"}}",
            [indexed]);

        Assert.False(result.IndexMayBeStale);
    }

    [Fact]
    public async Task ReadAsync_RetainsNewestTwentyFourMessages()
    {
        var content = string.Join('\n', Enumerable.Range(0, 30).Select(index =>
            $"{{\"type\":\"user.message\",\"data\":{{\"content\":\"message-{index}\"}}}}"));
        var result = await ReadAsync(content, []);

        Assert.Equal(24, result.DirectUserMessages.Count);
        Assert.Equal("message-6", result.DirectUserMessages[0].Message);
        Assert.Equal("message-29", result.DirectUserMessages[^1].Message);
    }

    [Fact]
    public async Task ReadAsync_SkipsMalformedAndOversizedLines()
    {
        var content = "not-json\n" + new string('x', 1_048_577) +
            "\n{\"type\":\"user.message\",\"data\":{\"text\":\"latest\"}}\n";
        var result = await ReadAsync(content, []);

        Assert.True(result.Truncated);
        Assert.Contains(result.DirectUserMessages, item => item.Message == "latest");
    }

    private static async Task<RawSessionEventTail> ReadAsync(
        string content,
        IReadOnlyList<Turn> turns)
    {
        var path = $@"{Root}\{SessionId}\events.jsonl";
        var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [path] = new(content),
        });
        var reader = new RawSessionEventTailReader(
            new NarniaOptions { SessionStatePath = Root },
            fileSystem);

        return await reader.ReadAsync(SessionId, turns, TestContext.Current.CancellationToken);
    }
}
