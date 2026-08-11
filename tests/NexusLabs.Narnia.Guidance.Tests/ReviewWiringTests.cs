namespace NexusLabs.Narnia.Guidance.Tests;

/// <summary>
/// The review procedure is only reproducible if the scripts and skill it names are present and
/// stay separated from the product surface published to users.
/// </summary>
public sealed class ReviewWiringTests
{
    private const string SkillPath = ".github/skills/review-changes/SKILL.md";

    [Theory]
    [InlineData(SkillPath)]
    [InlineData("scripts/guidance/Get-ApplicableInstructions.ps1")]
    [InlineData("scripts/guidance/InstructionGlob.Functions.ps1")]
    [InlineData("scripts/guidance/Get-ValidationInventory.ps1")]
    public void ReviewSurface_Exists(string relativePath)
    {
        Assert.True(RepositoryLayout.Exists(relativePath), $"'{relativePath}' is missing.");
    }

    [Fact]
    public void ReviewSkill_NamesTheScriptsItTellsTheReviewerToRun()
    {
        var skill = RepositoryLayout.ReadText(SkillPath);

        Assert.Contains("scripts/guidance/Get-ApplicableInstructions.ps1", skill, StringComparison.Ordinal);
        Assert.Contains("scripts/guidance/Get-ValidationInventory.ps1", skill, StringComparison.Ordinal);
    }

    [Fact]
    public void ReviewSkill_NamesTheChecksThatGateMerge()
    {
        var skill = RepositoryLayout.ReadText(SkillPath);
        var delivery = RepositoryLayout.ReadText(".github/genesis-delivery.json");

        using var document = System.Text.Json.JsonDocument.Parse(delivery);
        foreach (var check in document.RootElement.GetProperty("requiredChecks").EnumerateArray())
        {
            var name = check.GetString()!;
            Assert.Contains(name, skill, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ContributorSkills_AreNotPublishedToUsers()
    {
        var manifest = RepositoryLayout.ReadText("plugin.json");

        using var document = System.Text.Json.JsonDocument.Parse(manifest);
        var published = document.RootElement
            .GetProperty("skills")
            .EnumerateArray()
            .Select(entry => entry.GetString()!)
            .ToList();

        Assert.DoesNotContain(published, entry => SkillPath.StartsWith(entry, StringComparison.Ordinal));
    }

    [Fact]
    public void GuidanceScripts_RequireTheSharedGlobFunctions()
    {
        var resolver = RepositoryLayout.ReadText("scripts/guidance/Get-ApplicableInstructions.ps1");

        Assert.Contains("InstructionGlob.Functions.ps1", resolver, StringComparison.Ordinal);
    }
}
