using NexusLabs.Narnia.Core.Services;

namespace NexusLabs.Narnia.Core.Tests;

public sealed class DirectoryPathsTests
{
    // Git emits forward slashes on Windows while Narnia stores backslashes; both name one
    // directory, and treating them as different is what lets two sessions silently share a tree.
    [Theory]
    [InlineData(@"C:\dev\repo", "C:/dev/repo")]
    [InlineData(@"C:\dev\repo", @"C:\dev\repo\")]
    [InlineData(@"C:\dev\repo", @"C:\dev\repo")]
    [InlineData(@"C:\dev\repo", @"C:\dev\sub\..\repo")]
    [InlineData(@"C:\dev\repo", @"  C:\dev\repo  ")]
    public void AreSame_EquivalentSpellings_Match(string left, string right)
    {
        Assert.True(DirectoryPaths.AreSame(left, right));
    }

    [Theory]
    [InlineData(@"C:\dev\repo", @"C:\dev\repo-two")]
    [InlineData(@"C:\dev\repo", @"C:\dev\repo\sub")]
    [InlineData(@"C:\dev\repo", "")]
    [InlineData(@"C:\dev\repo", null)]
    public void AreSame_DistinctOrMissingPaths_DoNotMatch(string left, string? right)
    {
        Assert.False(DirectoryPaths.AreSame(left, right));
    }

    [Fact]
    public void AreSame_CaseOnlyDifference_MatchesOnWindowsSemantics()
    {
        Assert.True(DirectoryPaths.AreSame(@"C:\Dev\Repo", @"c:\dev\repo"));
    }

    // Trimming the separator off a drive root would leave "C:", which names the drive's current
    // directory rather than its root — a different directory entirely.
    [Fact]
    public void Normalize_DriveRoot_KeepsItsSeparator()
    {
        var normalized = DirectoryPaths.Normalize(@"C:\");

        Assert.Equal(@"C:\", normalized);
    }

    [Fact]
    public void Normalize_BlankInput_ReturnsNull()
    {
        Assert.Null(DirectoryPaths.Normalize(null));
        Assert.Null(DirectoryPaths.Normalize("   "));
    }

    // A malformed path must degrade to the trimmed original rather than throwing into a caller
    // that is only trying to compare two strings.
    [Fact]
    public void Normalize_InvalidPath_FallsBackToTrimmedInput()
    {
        var invalid = "C:\\dev\\re\0po";

        Assert.Equal(invalid, DirectoryPaths.Normalize(invalid));
    }
}
