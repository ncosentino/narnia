using NexusLabs.Narnia.Core.Models;

namespace NexusLabs.Narnia.Core.Tests;

public sealed class CommitShaQueryTests
{
    [Theory]
    [InlineData("abcd")]
    [InlineData("abc123")]
    [InlineData("deadc0de1234567890abcdef1234567890dead")]
    public void TryParse_ValidHexWithinLengthBounds_ReturnsQuery(string value)
    {
        var result = CommitShaQuery.TryParse(value);

        Assert.NotNull(result);
        Assert.Equal(value.ToLowerInvariant(), result.Value);
    }

    [Fact]
    public void TryParse_TrimsSurroundingWhitespace()
    {
        var result = CommitShaQuery.TryParse("  abc123  ");

        Assert.NotNull(result);
        Assert.Equal("abc123", result.Value);
    }

    [Fact]
    public void TryParse_UppercaseInput_NormalizedToLowercase()
    {
        var result = CommitShaQuery.TryParse("ABC123");

        Assert.NotNull(result);
        Assert.Equal("abc123", result.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryParse_NullOrEmpty_ReturnsNull(string? value)
    {
        Assert.Null(CommitShaQuery.TryParse(value));
    }

    [Theory]
    [InlineData("a")]
    [InlineData("ab")]
    [InlineData("abc")]
    public void TryParse_ShorterThanMinLength_ReturnsNull(string value)
    {
        Assert.Null(CommitShaQuery.TryParse(value));
    }

    [Fact]
    public void TryParse_LongerThanMaxLength_ReturnsNull()
    {
        var tooLong = new string('a', CommitShaQuery.MaxLength + 1);

        Assert.Null(CommitShaQuery.TryParse(tooLong));
    }

    [Fact]
    public void TryParse_ExactlyMaxLength_ReturnsQuery()
    {
        var exact = new string('a', CommitShaQuery.MaxLength);

        Assert.NotNull(CommitShaQuery.TryParse(exact));
    }

    [Theory]
    [InlineData("zzzz")]
    [InlineData("abc\"def")]
    [InlineData("abc:def")]
    [InlineData("abc)(def")]
    [InlineData("NOT abcd")]
    [InlineData("abcd OR wxyz")]
    [InlineData("*")]
    [InlineData("abcd-1234")]
    public void TryParse_NonHexCharacters_ReturnsNull(string value)
    {
        Assert.Null(CommitShaQuery.TryParse(value));
    }
}
