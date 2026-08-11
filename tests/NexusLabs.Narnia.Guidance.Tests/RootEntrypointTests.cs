namespace NexusLabs.Narnia.Guidance.Tests;

/// <summary>
/// Root guidance files load in every agent session, so their cost is paid by every task
/// regardless of relevance. These budgets keep detail in the surfaces that load on demand.
/// </summary>
public sealed class RootEntrypointTests
{
    private const int MaxAgentsLines = 60;
    private const int MaxAgentsBytes = 3072;

    [Fact]
    public void AgentsFile_StaysWithinItsAlwaysLoadedBudget()
    {
        var content = RepositoryLayout.ReadText("AGENTS.md");

        Assert.True(
            GuidanceContract.CountLines(content) <= MaxAgentsLines,
            $"AGENTS.md is {GuidanceContract.CountLines(content)} lines; the budget is {MaxAgentsLines}. " +
            "Move exact per-file rules to .github/instructions/ and rationale to docs/.");
        Assert.True(
            GuidanceContract.Utf8ByteCount(content) <= MaxAgentsBytes,
            $"AGENTS.md is {GuidanceContract.Utf8ByteCount(content)} UTF-8 bytes; the budget is {MaxAgentsBytes}.");
    }

    [Fact]
    public void AgentsFile_RoutesToTheTrustedSources()
    {
        var content = RepositoryLayout.ReadText("AGENTS.md");

        Assert.Contains("docs/index.md", content, StringComparison.Ordinal);
        Assert.Contains(".github/instructions/", content, StringComparison.Ordinal);
        Assert.Contains(".github/skills/review-changes/SKILL.md", content, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("CLAUDE.md")]
    [InlineData(".github/copilot-instructions.md")]
    public void HarnessRedirect_ExistsAndOnlyRedirects(string relativePath)
    {
        Assert.True(
            RepositoryLayout.Exists(relativePath),
            $"'{relativePath}' is missing; harnesses that do not read AGENTS.md directly need a redirect.");

        var content = RepositoryLayout.ReadText(relativePath);

        Assert.Contains("AGENTS.md", content, StringComparison.Ordinal);
        Assert.True(
            GuidanceContract.CountLines(content) <= 3,
            $"'{relativePath}' is {GuidanceContract.CountLines(content)} lines. A redirect points at " +
            "AGENTS.md rather than duplicating it, so guidance cannot drift between harnesses.");
    }
}
