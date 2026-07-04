namespace NexusLabs.Narnia.Core.Models;

/// <summary>
/// A validated, normalized commit-SHA (or SHA prefix) lookup term. Rejects anything that
/// cannot be a git object id -- non-hex characters, or a value too short to meaningfully
/// narrow a search -- before it reaches a data store. Without this guard a malformed value
/// could be misread as FTS5 query syntax (quotes, "OR"/"NOT", column filters) or match a
/// meaninglessly broad slice of unrelated content (e.g. a single hex character matches
/// roughly half of all session content).
/// </summary>
public sealed record CommitShaQuery
{
    /// <summary>
    /// Git's practical minimum for an unambiguous abbreviated SHA. Shorter values are
    /// rejected rather than silently returning a flood of unrelated matches.
    /// </summary>
    public const int MinLength = 4;

    /// <summary>Full SHA-1 hex length; nothing legitimate is ever longer.</summary>
    public const int MaxLength = 40;

    private CommitShaQuery(string value)
    {
        Value = value;
    }

    /// <summary>The trimmed, lowercased hex value.</summary>
    public string Value { get; }

    /// <summary>
    /// Parses and validates <paramref name="raw"/>, returning <see langword="null"/> if it is
    /// not a plausible (possibly abbreviated) commit SHA.
    /// </summary>
    public static CommitShaQuery? TryParse(string? raw)
    {
        var trimmed = raw?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return null;
        if (trimmed.Length is < MinLength or > MaxLength)
            return null;

        foreach (var c in trimmed)
        {
            if (!Uri.IsHexDigit(c))
                return null;
        }

        return new CommitShaQuery(trimmed.ToLowerInvariant());
    }
}
