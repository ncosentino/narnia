using NexusLabs.Narnia.Core.Models;

namespace NexusLabs.Narnia.Core.Services;

/// <summary>Deletes local sessions through the supported GitHub Copilot SDK interface.</summary>
public interface ICopilotSessionManager
{
    /// <summary>Deletes known local sessions using one SDK runtime connection.</summary>
    /// <param name="sessionIds">Session identifiers that already passed Narnia safety validation.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>One supported deletion result per requested session.</returns>
    ValueTask<IReadOnlyList<CopilotSessionDeletionResult>> DeleteSessionsAsync(
        IReadOnlyCollection<string> sessionIds,
        CancellationToken ct);
}
