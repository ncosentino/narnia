namespace NexusLabs.Narnia.Core.Services;

/// <summary>
/// Compares filesystem directory paths for "is this the same working tree" decisions.
/// </summary>
/// <remarks>
/// Git reports worktree paths with forward slashes even on Windows (<c>C:/dev/repo</c>), while
/// Narnia stores whatever the Copilot session store or the user typed (<c>C:\dev\repo</c>, possibly
/// with a trailing separator). Both spellings name one directory, so they must compare equal.
/// <para>
/// This is the opposite of <see cref="CopilotSidebarStatePath"/>, which hashes a working directory
/// byte-exactly because Copilot itself does. Do not reuse one for the other: normalizing there would
/// target the wrong workspace, and comparing byte-exactly here would miss real collisions.
/// </para>
/// </remarks>
public static class DirectoryPaths
{
    /// <summary>
    /// Reduces a path to a canonical form for comparison: separators unified to the host
    /// convention, redundant segments removed, and any trailing separator dropped (except for a
    /// drive or filesystem root, where the separator is significant).
    /// </summary>
    /// <param name="path">The path to canonicalize; may be <c>null</c> or blank.</param>
    /// <returns>The canonical form, or <c>null</c> when there was nothing to canonicalize.</returns>
    public static string? Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        var trimmed = path.Trim();
        try
        {
            // GetFullPath unifies separators and collapses "." / ".." without touching the disk.
            var full = Path.GetFullPath(trimmed);
            var trimmedEnd = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            // "C:\" and "/" collapse to "C:" and "" without their separator, which no longer names
            // the root, so keep the original spelling in that case.
            return trimmedEnd.Length == 0 || trimmedEnd.EndsWith(':') ? full : trimmedEnd;
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return trimmed;
        }
    }

    /// <summary>Determines whether two paths name the same directory.</summary>
    /// <param name="left">First path; may be <c>null</c>.</param>
    /// <param name="right">Second path; may be <c>null</c>.</param>
    /// <returns><c>true</c> when both are non-blank and canonicalize identically.</returns>
    public static bool AreSame(string? left, string? right)
    {
        var normalizedLeft = Normalize(left);
        var normalizedRight = Normalize(right);
        if (normalizedLeft is null || normalizedRight is null)
            return false;

        // Windows and macOS path lookups are case-insensitive; Linux is not. Narnia is
        // Windows-first, and a case-only difference far more often means "same directory,
        // different spelling" than two genuinely distinct trees.
        return string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase);
    }
}
