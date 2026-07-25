using NexusLabs.Narnia.Core.Models;

namespace NexusLabs.Narnia.Core.Repositories;

/// <summary>Inspects the minimum persisted event contract required for safe Copilot resume.</summary>
public interface ISessionResumeSafetyReader
{
    /// <summary>Reads local session state without modifying Copilot-owned files.</summary>
    /// <param name="sessionId">Copilot session identifier.</param>
    /// <returns>Resume-safety assessment with concrete evidence.</returns>
    SessionResumeAssessment Inspect(string sessionId);
}
