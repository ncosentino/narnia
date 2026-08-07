using System.Security.Cryptography;
using System.Text;

namespace NexusLabs.Narnia.Core.Services;

/// <summary>
/// Derives the file name Copilot uses for a workspace's sidebar tab list.
/// </summary>
public static class CopilotSidebarStatePath
{
    /// <summary>Extension Copilot writes sidebar state files with.</summary>
    public const string Extension = ".json";

    /// <summary>
    /// Builds the state file name for a working directory.
    /// </summary>
    /// <remarks>
    /// Copilot hashes the working directory byte-for-byte: no case folding, no separator
    /// normalization, and no trailing-separator trimming. Two spellings of the same folder
    /// therefore produce two independent tab lists, so the caller's string must be the exact
    /// value Copilot recorded.
    /// </remarks>
    /// <param name="cwd">Working directory exactly as Copilot recorded it.</param>
    /// <returns>Lowercase hex SHA-256 file name including the extension.</returns>
    public static string FileNameFor(string cwd)
    {
        ArgumentNullException.ThrowIfNull(cwd);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(cwd));
        return string.Concat(Convert.ToHexStringLower(hash), Extension);
    }
}
