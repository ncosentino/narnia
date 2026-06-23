using System.Security.Cryptography;
using System.Text;

namespace NexusLabs.Narnia.Core.Services;

/// <summary>
/// Computes a stable key identifying a terminal window by the set of session ids it
/// contains, independent of tab order. Used to deduplicate closed windows so reopening
/// the same working set continues a single logical record rather than piling up duplicates.
/// </summary>
public static class TerminalWindowComposition
{
    /// <summary>
    /// Returns a stable, order-independent key for the given session ids. The same set of
    /// ids always yields the same key; differing sets yield different keys.
    /// </summary>
    /// <param name="sessionIds">The session ids comprising the window.</param>
    /// <returns>A lowercase hex SHA-256 hash of the normalized, sorted id set.</returns>
    public static string Key(IEnumerable<string> sessionIds)
    {
        var normalized = sessionIds
            .Select(id => id.Trim().ToLowerInvariant())
            .Where(id => id.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal);

        var joined = string.Join('\n', normalized);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(joined));
        return Convert.ToHexStringLower(hash);
    }
}
