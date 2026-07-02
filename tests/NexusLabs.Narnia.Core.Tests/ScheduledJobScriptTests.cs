using NexusLabs.Narnia.Core.Services;

namespace NexusLabs.Narnia.Core.Tests;

public sealed class ScheduledJobScriptTests
{
    [Fact]
    public void Build_InvokesCopilotWithPrompt_AndLogs()
    {
        var script = ScheduledJobScript.Build(
            name: "Sample Daily",
            prompt: "Run example-issue-radar with --lookback 24h",
            workingDirectory: null,
            allowFlags: "--allow-all-tools --allow-all-paths",
            copilotArgs: null,
            logDirectory: @"C:\narnia\schedules\abc\logs");

        Assert.Contains("& copilot -p $prompt --allow-all-tools --allow-all-paths", script);
        Assert.Contains("Run example-issue-radar with --lookback 24h", script);
        Assert.Contains(@"$logDir = 'C:\narnia\schedules\abc\logs'", script);
        Assert.Contains("exit $code", script);
        Assert.DoesNotContain("Set-Location", script);
    }

    [Fact]
    public void Build_WithWorkingDirectory_SetsLocation()
    {
        var script = ScheduledJobScript.Build(
            "J", "hi", @"C:\dev\repo", null, null, @"C:\logs");

        Assert.Contains(@"Set-Location -LiteralPath 'C:\dev\repo'", script);
    }

    [Fact]
    public void Build_AppendsCopilotArgs()
    {
        var script = ScheduledJobScript.Build(
            "J", "hi", null, "--allow-all-tools", "--model gpt-5", @"C:\logs");

        Assert.Contains("& copilot -p $prompt --allow-all-tools --model gpt-5", script);
    }

    [Fact]
    public void Build_PromptWithQuotesAndDollar_IsEmbeddedVerbatimInHereString()
    {
        // A single-quoted here-string carries the text literally, so quotes/$ need no escaping and
        // create no injection surface. The only terminator is a line that is exactly '@.
        var prompt = "Say \"hi $env:USERNAME\" and don't 'break'";
        var script = ScheduledJobScript.Build("J", prompt, null, null, null, @"C:\logs");

        Assert.Contains("$prompt = @'", script);
        Assert.Contains(prompt, script);
    }

    [Fact]
    public void Build_MultiLinePrompt_PreservesAllLines()
    {
        var script = ScheduledJobScript.Build("J", "line one\nline two", null, null, null, @"C:\logs");

        Assert.Contains("line one", script);
        Assert.Contains("line two", script);
    }
}
