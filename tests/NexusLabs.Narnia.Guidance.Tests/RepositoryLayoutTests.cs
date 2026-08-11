namespace NexusLabs.Narnia.Guidance.Tests;

/// <summary>
/// Repository enumeration feeds every other structural test, so a defect here would silently
/// weaken all of them rather than fail loudly.
/// </summary>
public sealed class RepositoryLayoutTests
{
    [Fact]
    public void AllFiles_FindsTheGuidanceSurfacesTheContractDependsOn()
    {
        var files = RepositoryLayout.AllFiles();

        Assert.Contains("AGENTS.md", files);
        Assert.Contains("mkdocs.yml", files);
        Assert.Contains(".github/skills/review-changes/SKILL.md", files);
        Assert.NotEmpty(RepositoryLayout.InstructionFiles());
        Assert.NotEmpty(RepositoryLayout.DocumentationFiles());
    }

    [Fact]
    public void AllFiles_ExcludesBuildOutput()
    {
        Assert.DoesNotContain(
            RepositoryLayout.AllFiles(),
            path =>
                path.Contains("/obj/", StringComparison.Ordinal) ||
                path.Contains("/bin/", StringComparison.Ordinal) ||
                path.StartsWith("site/", StringComparison.Ordinal));
    }

    [Fact]
    public void AllFiles_UsesForwardSlashesRegardlessOfPlatform()
    {
        Assert.DoesNotContain(RepositoryLayout.AllFiles(), path => path.Contains('\\'));
    }

    [Fact]
    public void DirectoryWalkFallback_ProducesTheSameGuidanceSurfaces()
    {
        var walked = RepositoryLayout.FromDirectoryWalk();

        Assert.Contains("AGENTS.md", walked);
        Assert.Contains(".github/skills/review-changes/SKILL.md", walked);
        Assert.DoesNotContain(walked, path => path.Contains('\\'));
        Assert.DoesNotContain(
            walked,
            path =>
                path.Contains("/obj/", StringComparison.Ordinal) ||
                path.StartsWith("site/", StringComparison.Ordinal));
    }
}
