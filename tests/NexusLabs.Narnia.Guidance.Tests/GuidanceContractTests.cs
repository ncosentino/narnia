namespace NexusLabs.Narnia.Guidance.Tests;

/// <summary>
/// Negative fixtures for the guidance contract helpers. A structural gate that only ever runs
/// against a compliant repository proves nothing, so these prove each rule rejects the violation
/// it exists to catch.
/// </summary>
public sealed class GuidanceContractTests
{
    [Theory]
    [InlineData("", 0)]
    [InlineData("one", 1)]
    [InlineData("one\n", 1)]
    [InlineData("one\ntwo", 2)]
    [InlineData("one\ntwo\n", 2)]
    public void CountLines_DoesNotCountTheTrailingNewlineAsALine(string text, int expected)
    {
        Assert.Equal(expected, GuidanceContract.CountLines(text));
    }

    [Fact]
    public void Utf8ByteCount_CountsEncodedBytesNotCharacters()
    {
        Assert.Equal(3, GuidanceContract.Utf8ByteCount("—"));
    }

    [Fact]
    public void FrontmatterValue_ReadsAQuotedScalar()
    {
        const string content = "---\napplyTo: \"src/**\"\n---\n\n# Title\n";

        Assert.Equal("src/**", GuidanceContract.FrontmatterValue(content, "applyTo"));
    }

    [Fact]
    public void FrontmatterValue_IgnoresAMatchingLineInTheBody()
    {
        const string content = "---\ndescription: x\n---\n\napplyTo: src/**\n";

        Assert.Null(GuidanceContract.FrontmatterValue(content, "applyTo"));
    }

    [Fact]
    public void FrontmatterValue_ReturnsNullWithoutFrontmatter()
    {
        Assert.Null(GuidanceContract.FrontmatterValue("# Title\n", "applyTo"));
    }

    [Theory]
    [InlineData("src/**", "src/a.cs", true)]
    [InlineData("src/**", "src/nested/deep/a.cs", true)]
    [InlineData("src/**", "tests/a.cs", false)]
    [InlineData("src/*.cs", "src/a.cs", true)]
    [InlineData("src/*.cs", "src/nested/a.cs", false)]
    [InlineData("docs/**/*.md", "docs/a.md", true)]
    [InlineData("docs/**/*.md", "docs/nested/a.md", true)]
    [InlineData("src/**/*.{cs,razor}", "src/a.razor", true)]
    [InlineData("src/**/*.{cs,razor}", "src/a.js", false)]
    [InlineData("src/a.cs, tests/b.cs", "tests/b.cs", true)]
    [InlineData("src/?.cs", "src/a.cs", true)]
    [InlineData("src/?.cs", "src/ab.cs", false)]
    public void MatchesGlob_FollowsTheResolverSemantics(string applyTo, string path, bool expected)
    {
        Assert.Equal(expected, GuidanceContract.MatchesGlob(applyTo, path));
    }

    [Fact]
    public void MatchesGlob_NormalizesWindowsSeparators()
    {
        Assert.True(GuidanceContract.MatchesGlob("src/**", @"src\nested\a.cs"));
    }

    [Theory]
    [InlineData("src/{a.cs")]
    [InlineData("src/a.cs}")]
    public void SplitPatterns_RejectsUnbalancedBraces(string applyTo)
    {
        Assert.Throws<FormatException>(() => GuidanceContract.SplitPatterns(applyTo));
    }

    [Fact]
    public void LocalLinkTargets_ExcludesExternalAndFragmentOnlyLinks()
    {
        const string markdown = """
            [a](../docs/a.md)
            [b](https://example.com/b.md)
            [c](#section)
            [d](mailto:someone@example.com)
            [e](//example.com/e.md)
            [f](b.md#section)
            """;

        Assert.Equal(
            ["../docs/a.md", "b.md#section"],
            GuidanceContract.LocalLinkTargets(markdown));
    }

    [Fact]
    public void HeadingAnchors_IgnoreCommentedHeadingsInsideCodeFences()
    {
        const string markdown = """
            # Real Heading

            ```bash
            # Not a heading
            ```

            ## Second Heading
            """;

        Assert.Equal(["real-heading", "second-heading"], GuidanceContract.HeadingAnchors(markdown));
    }

    [Theory]
    [InlineData("Why not `~/.copilot`", "why-not-copilot")]
    [InlineData("Overrides never hide recorded values", "overrides-never-hide-recorded-values")]
    [InlineData("DbUp API notes", "dbup-api-notes")]
    [InlineData("The Copilot-owned session store", "the-copilot-owned-session-store")]
    public void HeadingSlug_MatchesTheGeneratedAnchor(string heading, string expected)
    {
        Assert.Equal(expected, GuidanceContract.HeadingSlug(heading));
    }

    [Fact]
    public void NavigationTargets_ReadOnlyTheNavBlock()
    {
        const string configuration = """
            site_name: Example
            nav:
              - Home: index.md
              - Section:
                - Nested: nested/page.md
            plugins:
              - search
            hooks:
              - docs/hooks/ignored.md
            """;

        Assert.Equal(["index.md", "nested/page.md"], GuidanceContract.NavigationTargets(configuration));
    }
}
