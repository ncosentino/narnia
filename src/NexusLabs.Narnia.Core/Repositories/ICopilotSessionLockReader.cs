namespace NexusLabs.Narnia.Core.Repositories;

/// <summary>Reads every session-state lock associated with a known Copilot process.</summary>
public interface ICopilotSessionLockReader
{
    /// <summary>Gets all session identifiers whose lock filename contains the process identifier.</summary>
    /// <param name="copilotProcessId">Verified live Copilot runtime process identifier.</param>
    /// <returns>Every session directory associated with the process, including subagent sessions.</returns>
    IReadOnlyList<string> GetSessionIds(int copilotProcessId);
}
