namespace NexusLabs.Narnia.Core.Repositories;

/// <summary>Reads every session-state lock associated with a known Copilot process.</summary>
public interface ICopilotSessionLockReader
{
    /// <summary>Gets all session identifiers whose lock filename contains the process identifier.</summary>
    /// <param name="copilotProcessId">Verified live Copilot runtime process identifier.</param>
    /// <returns>
    /// Every session directory associated with the process, including provisional, nested,
    /// and background session state.
    /// </returns>
    IReadOnlyList<string> GetSessionIds(int copilotProcessId);

    /// <summary>Reads matching locks for several verified Copilot processes in one filesystem pass.</summary>
    /// <param name="copilotProcessIds">Verified live Copilot runtime process identifiers.</param>
    /// <returns>Session identifiers grouped by process identifier.</returns>
    IReadOnlyDictionary<int, IReadOnlyList<string>> GetSessionIdsByProcess(
        IReadOnlyCollection<int> copilotProcessIds);
}
