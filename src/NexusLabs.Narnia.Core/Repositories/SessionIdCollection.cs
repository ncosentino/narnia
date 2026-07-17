namespace NexusLabs.Narnia.Core.Repositories;

internal static class SessionIdCollection
{
    public static IReadOnlyList<string> Normalize(IEnumerable<string> sessionIds)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>();
        foreach (var sessionId in sessionIds)
        {
            if (string.IsNullOrWhiteSpace(sessionId) || !seen.Add(sessionId))
                continue;

            result.Add(sessionId);
        }

        return result;
    }
}
