using System.Text.RegularExpressions;

namespace NexusLabs.Narnia.Guidance.Tests;

/// <summary>
/// Instruction files are recurring context: every edit to a matching file pays for the whole
/// body. These tests keep each instruction scoped to one real population and keep the total
/// context a single edit loads within budget.
/// </summary>
public sealed partial class InstructionTests
{
    private const int IndividualReviewLines = 100;
    private const int IndividualReviewBytes = 8192;
    private const int MatchedContextTargetLines = 300;
    private const int MatchedContextTargetBytes = 16384;

    [Fact]
    public void InstructionsExist()
    {
        Assert.NotEmpty(RepositoryLayout.InstructionFiles());
    }

    [Fact]
    public void EveryInstruction_DeclaresAUsableApplyTo()
    {
        foreach (var instruction in RepositoryLayout.InstructionFiles())
        {
            var applyTo = GuidanceContract.FrontmatterValue(
                RepositoryLayout.ReadText(instruction),
                "applyTo");

            Assert.False(
                string.IsNullOrWhiteSpace(applyTo),
                $"'{instruction}' has no applyTo value, so it never reaches an edit.");

            var exception = Record.Exception(() => GuidanceContract.SplitPatterns(applyTo!));
            Assert.True(
                exception is null,
                $"'{instruction}' has an invalid applyTo '{applyTo}': {exception?.Message}");
        }
    }

    [Fact]
    public void EveryInstruction_MatchesFilesThatActuallyExist()
    {
        var files = RepositoryLayout.AllFiles();

        foreach (var instruction in RepositoryLayout.InstructionFiles())
        {
            var applyTo = GuidanceContract.FrontmatterValue(
                RepositoryLayout.ReadText(instruction),
                "applyTo")!;

            Assert.Contains(
                files,
                file => GuidanceContract.MatchesGlob(applyTo, file));
        }
    }

    [Fact]
    public void OversizedInstructions_CarryAReviewThresholdReason()
    {
        foreach (var instruction in RepositoryLayout.InstructionFiles())
        {
            var content = RepositoryLayout.ReadText(instruction);
            var lines = GuidanceContract.CountLines(content);
            var bytes = GuidanceContract.Utf8ByteCount(content);
            if (lines <= IndividualReviewLines && bytes <= IndividualReviewBytes)
                continue;

            Assert.False(
                string.IsNullOrWhiteSpace(
                    GuidanceContract.FrontmatterValue(content, "reviewThresholdReason")),
                $"'{instruction}' is {lines} lines/{bytes} bytes, past the {IndividualReviewLines}-line/" +
                $"{IndividualReviewBytes}-byte review threshold, and gives no reviewThresholdReason. " +
                "Split it, move rationale to docs/, or record why the size is justified.");
        }
    }

    [Fact]
    public void NoFile_LoadsMoreMatchedInstructionContextThanTheBudget()
    {
        var instructions = RepositoryLayout
            .InstructionFiles()
            .Select(path =>
            {
                var content = RepositoryLayout.ReadText(path);
                return new
                {
                    Path = path,
                    ApplyTo = GuidanceContract.FrontmatterValue(content, "applyTo")!,
                    Lines = GuidanceContract.CountLines(content),
                    Bytes = GuidanceContract.Utf8ByteCount(content),
                };
            })
            .ToList();

        var failures = new List<string>();
        foreach (var file in RepositoryLayout.AllFiles())
        {
            var matched = instructions
                .Where(instruction => GuidanceContract.MatchesGlob(instruction.ApplyTo, file))
                .ToList();
            if (matched.Count == 0)
                continue;

            var lines = matched.Sum(instruction => instruction.Lines);
            var bytes = matched.Sum(instruction => instruction.Bytes);
            if (lines > MatchedContextTargetLines || bytes > MatchedContextTargetBytes)
                failures.Add($"{file}: {lines} lines/{bytes} bytes from {string.Join(", ", matched.Select(m => m.Path))}");
        }

        Assert.True(
            failures.Count == 0,
            $"These files load more than {MatchedContextTargetLines} lines or {MatchedContextTargetBytes} bytes " +
            "of instruction context on every edit:" + Environment.NewLine +
            string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Instructions_DoNotClaimToImportEachOther()
    {
        foreach (var instruction in RepositoryLayout.InstructionFiles())
        {
            var body = RepositoryLayout.ReadText(instruction);
            var frontmatter = GuidanceContract.FrontmatterBlock(body);
            if (frontmatter is not null)
                body = body.Replace(frontmatter, string.Empty, StringComparison.Ordinal);

            Assert.False(
                InstructionReferencePattern().IsMatch(body),
                $"'{instruction}' references another instruction file. Instruction clients provide no import " +
                "mechanism, so a reader may never load it. Share rules through overlapping globs instead.");
        }
    }

    [GeneratedRegex(@"[A-Za-z0-9._/-]+\.instructions\.md")]
    private static partial Regex InstructionReferencePattern();
}
