using NexusLabs.Narnia.Core.Models;

namespace NexusLabs.Narnia.Core.Repositories;

/// <summary>Reads task state from a Copilot-owned session workspace without modifying it.</summary>
public interface ISessionTaskStateReader
{
    /// <summary>Reads known task and dependency tables in read-only mode.</summary>
    /// <param name="sessionId">Copilot session identifier.</param>
    /// <returns>Recovered task state or an explicit read error.</returns>
    SessionTaskState Read(string sessionId);
}
