using System.IO.Abstractions.TestingHelpers;
using System.Text;
using NexusLabs.Narnia.Core.Configuration;
using NexusLabs.Narnia.Core.Models;
using NexusLabs.Narnia.Core.Services;

namespace NexusLabs.Narnia.Core.Tests;

public sealed class ScheduledRunOutcomeReaderTests
{
    private const string SchedulesDir = @"C:\narnia\schedules";
    private const string SessionStateDir = @"C:\copilot\session-state";
    private const string SessionId = "1b7cf2d0-9d2b-4f0d-9d8f-6b0f1e2a3c4d";
    private const string LogPath = @"C:\narnia\schedules\job-1\logs\run-2026-08-08_003003.log";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static NarniaOptions Options() => new()
    {
        SchedulesDirectory = SchedulesDir,
        SessionStatePath = SessionStateDir,
    };

    private static ScheduledRunOutcomeReader Create(MockFileSystem fs) =>
        new(Options(), new ScheduledJobWorkspace(Options(), fs), fs);

    [Fact]
    public async Task ReadLatestAsync_SessionEndedInAnAbort_IsInterrupted()
    {
        var fs = FileSystemWith(
            log: RunLog(SessionId),
            events: string.Join(
                '\n',
                Event("assistant.turn_start"),
                Event("tool.execution_start"),
                """{"type":"abort","data":{"reason":"user_initiated"}}""",
                Event("session.shutdown")));

        var outcome = await Create(fs).ReadLatestAsync("job-1", Ct);

        Assert.Equal(ScheduledRunCompletion.Interrupted, outcome.Completion);
        Assert.Equal(SessionId, outcome.SessionId);
        Assert.Equal("user_initiated", outcome.AbortReason);
        Assert.True(outcome.WasInterrupted);
    }

    [Fact]
    public async Task ReadLatestAsync_SessionRanToCompletion_IsCompleted()
    {
        var fs = FileSystemWith(
            log: RunLog(SessionId),
            events: string.Join(
                '\n',
                Event("assistant.turn_end"),
                Event("session.usage_checkpoint"),
                Event("session.shutdown")));

        var outcome = await Create(fs).ReadLatestAsync("job-1", Ct);

        Assert.Equal(ScheduledRunCompletion.Completed, outcome.Completion);
        Assert.False(outcome.WasInterrupted);
    }

    [Fact]
    public async Task ReadLatestAsync_NoLogYet_IsIndeterminate()
    {
        var outcome = await Create(new MockFileSystem()).ReadLatestAsync("job-1", Ct);

        Assert.Equal(ScheduledRunCompletion.Unknown, outcome.Completion);
        Assert.Null(outcome.SessionId);
    }

    [Fact]
    public async Task ReadLatestAsync_LogNamesNoSession_IsIndeterminate()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [LogPath] = new MockFileData("=== Job ===\nEnd: ExitCode: 0"),
        });

        var outcome = await Create(fs).ReadLatestAsync("job-1", Ct);

        Assert.Equal(ScheduledRunCompletion.Unknown, outcome.Completion);
        Assert.Null(outcome.SessionId);
    }

    [Fact]
    public async Task ReadLatestAsync_SessionFolderIsGone_ReportsUnknownButKeepsTheSessionId()
    {
        // Sessions can be cleaned up while the job's logs remain. That is not evidence the run was
        // interrupted, so it must not be reported as one.
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [LogPath] = new MockFileData(RunLog(SessionId)),
        });

        var outcome = await Create(fs).ReadLatestAsync("job-1", Ct);

        Assert.Equal(ScheduledRunCompletion.Unknown, outcome.Completion);
        Assert.Equal(SessionId, outcome.SessionId);
    }

    [Fact]
    public async Task ReadLatestAsync_SessionIdEscapingTheSessionStateRoot_IsRejected()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [LogPath] = new MockFileData(@"Resume  copilot --resume=..\..\Windows\System32\config"),
        });

        var outcome = await Create(fs).ReadLatestAsync("job-1", Ct);

        Assert.Equal(ScheduledRunCompletion.Unknown, outcome.Completion);
        Assert.Null(outcome.SessionId);
    }

    [Fact]
    public async Task ReadLatestAsync_ReadsOnlyTheTailOfAVeryLargeEventStream()
    {
        // Event streams reach hundreds of megabytes and this runs on every schedule listing, so the
        // classification has to come from the end of the file without reading the whole thing.
        var filler = new StringBuilder();
        for (var i = 0; i < 40_000; i++)
            filler.Append(Event("assistant.turn_end")).Append('\n');

        var events = filler
            .Append(Event("tool.execution_start")).Append('\n')
            .Append("""{"type":"abort","data":{"reason":"user_initiated"}}""").Append('\n')
            .Append(Event("session.shutdown"))
            .ToString();

        var fs = FileSystemWith(RunLog(SessionId), events);
        Assert.True(events.Length > 1_000_000);

        var outcome = await Create(fs).ReadLatestAsync("job-1", Ct);

        Assert.Equal(ScheduledRunCompletion.Interrupted, outcome.Completion);
    }

    [Fact]
    public async Task ReadLatestAsync_UsesTheNewestRunLog()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [@"C:\narnia\schedules\job-1\logs\run-2026-08-01_003003.log"] =
                new MockFileData(RunLog("00000000-0000-4000-8000-000000000000")),
            [@"C:\narnia\schedules\job-1\logs\run-2026-08-08_003003.log"] =
                new MockFileData(RunLog(SessionId)),
            [$@"{SessionStateDir}\{SessionId}\events.jsonl"] =
                new MockFileData(Event("session.shutdown")),
        });

        var outcome = await Create(fs).ReadLatestAsync("job-1", Ct);

        Assert.Equal(SessionId, outcome.SessionId);
    }

    private static MockFileSystem FileSystemWith(string log, string events) =>
        new(new Dictionary<string, MockFileData>
        {
            [LogPath] = new MockFileData(log),
            [$@"{SessionStateDir}\{SessionId}\events.jsonl"] = new MockFileData(events),
        });

    private static string RunLog(string sessionId) =>
        $"=== Job ===\nChanges +1 -0\nResume     copilot --resume={sessionId}\nEnd: ExitCode: 0";

    private static string Event(string type) =>
        $$"""{"type":"{{type}}","timestamp":"2026-08-08T07:43:09.931Z"}""";
}
