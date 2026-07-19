using NexusLabs.Narnia.Core.Repositories;

namespace NexusLabs.Narnia.Core.Services;

/// <summary>Combines verified Copilot processes with their session-state locks.</summary>
public sealed class CopilotSessionActivityReader(
    ICopilotProcessProvider processProvider,
    ICopilotSessionLockReader lockReader) : ICopilotSessionActivityReader
{
    /// <inheritdoc />
    public IReadOnlySet<string> GetActiveSessionIds()
    {
        var sessionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var processIds = processProvider.GetProcessIds();
        foreach (var matches in lockReader.GetSessionIdsByProcess(processIds).Values)
            sessionIds.UnionWith(matches);
        return sessionIds;
    }
}
