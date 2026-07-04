using System.IO.Abstractions;

namespace NexusLabs.Narnia.Core.Services;

/// <summary>
/// Searches a <c>PATH</c>-style, <see cref="Path.PathSeparator"/>-delimited string for a named
/// executable. Pure and file-system-abstracted so it is unit-testable without touching the real
/// <c>PATH</c> environment variable or disk; the OS-specific caller (e.g. a Windows resolver) reads
/// the real environment variable and passes it in.
/// </summary>
public static class PathExecutableLocator
{
    /// <summary>
    /// Returns the full path to the first directory in <paramref name="pathValue"/> that contains
    /// <paramref name="fileName"/>, or <c>null</c> if <paramref name="pathValue"/> is empty or no
    /// directory contains it.
    /// </summary>
    public static string? Find(string fileName, string? pathValue, IFileSystem fileSystem)
    {
        if (string.IsNullOrWhiteSpace(pathValue))
            return null;

        foreach (var dir in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = fileSystem.Path.Combine(dir.Trim(), fileName);
            if (fileSystem.File.Exists(candidate))
                return candidate;
        }

        return null;
    }
}
