using System.Text;
using System.Text.RegularExpressions;

namespace NexusLabs.Narnia.Guidance.Tests;

/// <summary>
/// Pure helpers for the guidance structure contract. These are deliberately free of file-system
/// access so the rules they encode can be proven against synthetic input as well as the real
/// repository.
/// </summary>
internal static partial class GuidanceContract
{
    public static int CountLines(string text)
    {
        if (text.Length == 0)
            return 0;

        var trimmed = text.EndsWith('\n')
            ? text[..^1]
            : text;
        return trimmed.Split('\n').Length;
    }

    public static int Utf8ByteCount(string text) => Encoding.UTF8.GetByteCount(text);

    /// <summary>
    /// Reads a single scalar value from a leading YAML frontmatter block. Returns null when the
    /// document has no frontmatter or the key is absent.
    /// </summary>
    public static string? FrontmatterValue(string content, string name)
    {
        var block = FrontmatterBlock(content);
        if (block is null)
            return null;

        foreach (var rawLine in block.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            var separator = line.IndexOf(':');
            if (separator <= 0)
                continue;

            if (!line[..separator].Trim().Equals(name, StringComparison.Ordinal))
                continue;

            return line[(separator + 1)..].Trim().Trim('"', '\'');
        }

        return null;
    }

    public static string? FrontmatterBlock(string content)
    {
        var normalized = content.Replace("\r\n", "\n");
        if (!normalized.StartsWith("---\n", StringComparison.Ordinal))
            return null;

        var end = normalized.IndexOf("\n---", 3, StringComparison.Ordinal);
        return end < 0
            ? null
            : normalized[4..(end + 1)];
    }

    /// <summary>
    /// Matches an instruction <c>applyTo</c> glob against a repository-relative path using the
    /// same semantics as <c>scripts/guidance/InstructionGlob.Functions.ps1</c>: comma-separated
    /// patterns, brace alternatives, <c>**</c>, <c>*</c>, and <c>?</c>.
    /// </summary>
    public static bool MatchesGlob(string applyTo, string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/');
        foreach (var pattern in SplitPatterns(applyTo))
        {
            foreach (var expanded in ExpandBraces(pattern))
            {
                if (Regex.IsMatch(normalized, GlobToRegex(expanded)))
                    return true;
            }
        }

        return false;
    }

    public static IReadOnlyList<string> SplitPatterns(string applyTo)
    {
        var patterns = new List<string>();
        var current = new StringBuilder();
        var depth = 0;
        foreach (var character in applyTo)
        {
            switch (character)
            {
                case '{':
                    depth++;
                    break;
                case '}':
                    depth--;
                    if (depth < 0)
                        throw new FormatException($"Invalid glob '{applyTo}': unmatched closing brace.");
                    break;
            }

            if (character == ',' && depth == 0)
            {
                Append(patterns, current);
                continue;
            }

            current.Append(character);
        }

        if (depth != 0)
            throw new FormatException($"Invalid glob '{applyTo}': unmatched opening brace.");

        Append(patterns, current);
        return patterns;
    }

    public static string GlobToRegex(string pattern)
    {
        var escaped = Regex.Escape(pattern);
        escaped = escaped.Replace("\\*\\*/", "(?:.*/)?", StringComparison.Ordinal);
        escaped = escaped.Replace("\\*\\*", ".*", StringComparison.Ordinal);
        escaped = escaped.Replace("\\*", "[^/]*", StringComparison.Ordinal);
        escaped = escaped.Replace("\\?", "[^/]", StringComparison.Ordinal);
        return $"^{escaped}$";
    }

    /// <summary>
    /// Extracts inline Markdown link targets that point at repository content. External schemes,
    /// protocol-relative targets, and pure fragments are excluded.
    /// </summary>
    public static IReadOnlyList<string> LocalLinkTargets(string markdown)
    {
        var targets = new List<string>();
        foreach (Match match in LinkPattern().Matches(markdown))
        {
            var target = match.Groups[1].Value.Trim();
            if (target.Length == 0 || target.StartsWith('#'))
                continue;

            if (target.StartsWith("//", StringComparison.Ordinal) ||
                target.Contains("://", StringComparison.Ordinal) ||
                target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            targets.Add(target);
        }

        return targets;
    }

    /// <summary>
    /// Slugifies a Markdown heading the way the documentation toolchain generates anchors.
    /// </summary>
    public static string HeadingSlug(string heading)
    {
        var builder = new StringBuilder();
        foreach (var character in heading.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character) || character is '_' or '-')
                builder.Append(character);
            else if (char.IsWhiteSpace(character))
                builder.Append(' ');
        }

        return string.Join('-', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    public static IReadOnlyList<string> HeadingAnchors(string markdown)
    {
        var anchors = new List<string>();
        var inFence = false;
        foreach (var rawLine in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                inFence = !inFence;
                continue;
            }

            if (inFence || !line.StartsWith('#'))
                continue;

            var text = line.TrimStart('#').Trim();
            if (text.Length > 0)
                anchors.Add(HeadingSlug(text));
        }

        return anchors;
    }

    /// <summary>
    /// Collects the document paths referenced by a MkDocs <c>nav:</c> block. The block is read
    /// textually because the configuration carries Python tags a general YAML reader rejects.
    /// </summary>
    public static IReadOnlyList<string> NavigationTargets(string mkDocsConfiguration)
    {
        var targets = new List<string>();
        var inNav = false;
        foreach (var rawLine in mkDocsConfiguration.Replace("\r\n", "\n").Split('\n'))
        {
            if (rawLine.StartsWith("nav:", StringComparison.Ordinal))
            {
                inNav = true;
                continue;
            }

            if (!inNav)
                continue;

            if (rawLine.Length > 0 && !char.IsWhiteSpace(rawLine[0]))
                break;

            foreach (Match match in NavigationEntryPattern().Matches(rawLine))
                targets.Add(match.Value);
        }

        return targets;
    }

    private static IReadOnlyList<string> ExpandBraces(string pattern)
    {
        var open = pattern.IndexOf('{');
        if (open < 0)
            return [pattern];

        var depth = 0;
        var close = -1;
        for (var index = open; index < pattern.Length; index++)
        {
            if (pattern[index] == '{')
            {
                depth++;
            }
            else if (pattern[index] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    close = index;
                    break;
                }
            }
        }

        if (close < 0)
            throw new FormatException($"Invalid glob '{pattern}': unmatched opening brace.");

        var prefix = pattern[..open];
        var body = pattern[(open + 1)..close];
        var suffix = pattern[(close + 1)..];
        var alternatives = SplitPatterns(body);
        if (alternatives.Count == 0)
            throw new FormatException($"Invalid glob '{pattern}': empty brace alternatives.");

        var expanded = new List<string>();
        foreach (var alternative in alternatives)
            expanded.AddRange(ExpandBraces(prefix + alternative + suffix));

        return expanded;
    }

    private static void Append(List<string> patterns, StringBuilder current)
    {
        var value = current.ToString().Trim();
        if (value.Length > 0)
            patterns.Add(value);

        current.Clear();
    }

    [GeneratedRegex(@"\]\(([^)\s]+)(?:\s+""[^""]*"")?\)")]
    private static partial Regex LinkPattern();

    [GeneratedRegex(@"[A-Za-z0-9._/-]+\.md")]
    private static partial Regex NavigationEntryPattern();
}
