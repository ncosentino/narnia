using System.Text.Json;
using System.Text.RegularExpressions;

namespace NexusLabs.Narnia.Guidance.Tests;

public sealed class RunnerPolicyTests
{
    private static readonly string[] AllowedRunnerLabels =
    [
        "ubuntu-latest",
        "windows-latest",
    ];

    [Fact]
    public void DeliveryContract_RequiresStandardGitHubHostedRunners()
    {
        using var document = JsonDocument.Parse(
            RepositoryLayout.ReadText(".github/genesis-delivery.json"));
        var root = document.RootElement;

        Assert.Equal(2, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(
            "github-hosted-standard",
            root.GetProperty("runnerPolicy").GetString());
        Assert.False(root.TryGetProperty("runnerProfiles", out _));
        Assert.False(
            root.GetProperty("draftValidation")
                .TryGetProperty("pitcrewDefault", out _));

        using var schemaDocument = JsonDocument.Parse(
            RepositoryLayout.ReadText(".github/genesis-delivery.schema.json"));
        var schemaProperties = schemaDocument.RootElement.GetProperty("properties");
        Assert.Equal(
            "github-hosted-standard",
            schemaProperties.GetProperty("runnerPolicy").GetProperty("const").GetString());
        Assert.False(schemaProperties.TryGetProperty("runnerProfiles", out _));
    }

    [Fact]
    public void DeliveryConfiguration_RejectsOtherRunnerPolicies()
    {
        var script = RepositoryLayout.ReadText(
            "scripts/delivery/Configure-GitHubDelivery.ps1");

        Assert.Contains("github-hosted-standard", script, StringComparison.Ordinal);
        Assert.DoesNotContain("runnerProfiles", script, StringComparison.Ordinal);
        Assert.DoesNotContain("pitcrewDefault", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Workflows_UseOnlyApprovedStandardRunnerLabels()
    {
        var workflows = RepositoryLayout.AllFiles()
            .Where(path =>
                path.StartsWith(".github/workflows/", StringComparison.Ordinal) &&
                (path.EndsWith(".yml", StringComparison.Ordinal) ||
                 path.EndsWith(".yaml", StringComparison.Ordinal)))
            .ToArray();

        Assert.NotEmpty(workflows);
        foreach (var workflow in workflows)
        {
            var text = RepositoryLayout.ReadText(workflow);
            var matches = Regex.Matches(
                text,
                @"(?m)^[ \t]*runs-on:[ \t]*(?<label>[^\r\n#]+?)[ \t]*\r?$");

            Assert.NotEmpty(matches);
            foreach (Match match in matches)
            {
                var label = match.Groups["label"].Value.Trim().Trim('"', '\'');
                Assert.True(
                    AllowedRunnerLabels.Contains(label, StringComparer.Ordinal),
                    $"Workflow '{workflow}' uses unsupported runner label '{label}'.");
            }
        }
    }

    [Theory]
    [InlineData(".github/workflows/ci.yml")]
    [InlineData(".github/workflows/release.yml")]
    public void WindowsWorkflows_DocumentPublicRepositoryRunnerTerms(string workflow)
    {
        var text = RepositoryLayout.ReadText(workflow);

        Assert.Contains("windows-latest", text, StringComparison.Ordinal);
        Assert.Contains("free and unlimited for public repositories", text, StringComparison.Ordinal);
        Assert.Contains(
            "https://docs.github.com/en/actions/reference/runners/github-hosted-runners",
            text,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ContinuousIntegration_DoesNotRetainReleasePackages()
    {
        var workflow = RepositoryLayout.ReadText(".github/workflows/ci.yml");

        Assert.DoesNotContain("actions/upload-artifact", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseHandoffArtifact_ExpiresAfterOneDay()
    {
        var workflow = RepositoryLayout.ReadText(".github/workflows/release.yml");

        Assert.Contains("retention-days: 1", workflow, StringComparison.Ordinal);
    }
}
