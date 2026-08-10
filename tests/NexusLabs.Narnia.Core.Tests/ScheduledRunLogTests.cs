using NexusLabs.Narnia.Core.Services;

namespace NexusLabs.Narnia.Core.Tests;

public sealed class ScheduledRunLogTests
{
    [Fact]
    public void FindSessionId_ReadsTheResumeFooter()
    {
        const string log = """
            === Weekly Draft ===
            Start: 2026-08-08T00:30:03
            Changes    +142 -17
            Resume     copilot --resume=1b7cf2d0-9d2b-4f0d-9d8f-6b0f1e2a3c4d
            End: 2026-08-08T00:43:11 ExitCode: 0
            """;

        Assert.Equal("1b7cf2d0-9d2b-4f0d-9d8f-6b0f1e2a3c4d", ScheduledRunLog.FindSessionId(log));
    }

    [Fact]
    public void FindSessionId_PrefersTheLastMatch_BecauseThePromptIsEchoedFirst()
    {
        // A job's prompt is written into the top of its own log, so an earlier identifier can
        // belong to the prompt rather than to the run that just finished.
        const string log = """
            Prompt: resume the old work with copilot --resume=00000000-0000-4000-8000-000000000000
            Resume     copilot --resume=1b7cf2d0-9d2b-4f0d-9d8f-6b0f1e2a3c4d
            """;

        Assert.Equal("1b7cf2d0-9d2b-4f0d-9d8f-6b0f1e2a3c4d", ScheduledRunLog.FindSessionId(log));
    }

    [Fact]
    public void FindSessionId_AcceptsASpaceSeparatedFlag()
    {
        const string log = "Resume     copilot --resume 1b7cf2d0-9d2b-4f0d-9d8f-6b0f1e2a3c4d";

        Assert.Equal("1b7cf2d0-9d2b-4f0d-9d8f-6b0f1e2a3c4d", ScheduledRunLog.FindSessionId(log));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("=== Weekly Draft ===\nEnd: ExitCode: 0")]
    [InlineData("--resume=not-a-guid")]
    [InlineData("--resume=1b7cf2d0-9d2b-4f0d-9d8f")]
    public void FindSessionId_WithoutAWellFormedIdentifier_IsNull(string? log)
    {
        Assert.Null(ScheduledRunLog.FindSessionId(log));
    }
}
