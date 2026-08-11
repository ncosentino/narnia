using System.Text.RegularExpressions;

namespace NexusLabs.Narnia.Guidance.Tests;

/// <summary>
/// Architecture decision records are only trustworthy if they are complete, indexed, and
/// immutable once accepted. These tests enforce the shape described in docs/adr/index.md.
/// </summary>
public sealed partial class ArchitectureDecisionRecordTests
{
    private static readonly string[] RequiredSections =
    [
        "## Context",
        "## Decision",
        "## Alternatives considered",
        "## Consequences",
    ];

    private static IReadOnlyList<string> Records() =>
        [.. RepositoryLayout
            .AllFiles()
            .Where(path =>
                path.StartsWith("docs/adr/", StringComparison.Ordinal) &&
                path.EndsWith(".md", StringComparison.Ordinal) &&
                !path.Equals("docs/adr/index.md", StringComparison.Ordinal))];

    [Fact]
    public void RecordsExist()
    {
        Assert.NotEmpty(Records());
    }

    [Fact]
    public void RecordsAreIndexed()
    {
        var index = RepositoryLayout.ReadText("docs/adr/index.md");

        foreach (var record in Records())
        {
            var fileName = Path.GetFileName(record);
            Assert.Contains(fileName, index, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void EveryRecord_UsesTheNumberedNamingConvention()
    {
        foreach (var record in Records())
        {
            var fileName = Path.GetFileName(record);
            Assert.True(
                FileNamePattern().IsMatch(fileName),
                $"'{record}' does not follow the 'adr-NNNN-short-title.md' convention.");
        }
    }

    [Fact]
    public void RecordNumbers_AreUnique()
    {
        var duplicates = Records()
            .Select(record => FileNamePattern().Match(Path.GetFileName(record)).Groups[1].Value)
            .GroupBy(number => number, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        Assert.True(
            duplicates.Count == 0,
            "Duplicate ADR numbers: " + string.Join(", ", duplicates));
    }

    [Fact]
    public void EveryRecord_DeclaresTheRequiredMetadataAndSections()
    {
        foreach (var record in Records())
        {
            var content = RepositoryLayout.ReadText(record);

            Assert.False(
                string.IsNullOrWhiteSpace(GuidanceContract.FrontmatterValue(content, "description")),
                $"'{record}' has no frontmatter description, so it renders without a summary.");

            var number = FileNamePattern().Match(Path.GetFileName(record)).Groups[1].Value;
            Assert.Contains($"# ADR-{number}:", content, StringComparison.Ordinal);

            var status = StatusPattern().Match(content);
            Assert.True(status.Success, $"'{record}' has no '**Status:**' line.");

            var value = status.Groups[1].Value.Trim();
            Assert.True(
                value is "Proposed" or "Accepted" || value.StartsWith("Superseded by ADR-", StringComparison.Ordinal),
                $"'{record}' has an unsupported status '{value}'.");

            foreach (var section in RequiredSections)
                Assert.Contains(section, content, StringComparison.Ordinal);
        }
    }

    [GeneratedRegex(@"^adr-(\d{4})-[a-z0-9]+(?:-[a-z0-9]+)*\.md$")]
    private static partial Regex FileNamePattern();

    [GeneratedRegex(@"\*\*Status:\*\*\s*(.+)")]
    private static partial Regex StatusPattern();
}
