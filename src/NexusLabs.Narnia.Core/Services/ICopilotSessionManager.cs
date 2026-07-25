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

    /// <summary>Creates a valid event stream and seeds its first turn with recovered context.</summary>
    /// <param name="request">Session identifier, working directory, and bounded bootstrap context.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>Explicit supported-session creation result.</returns>
    ValueTask<CopilotRecoverySessionResult> CreateRecoverySessionAsync(
        CopilotRecoverySessionRequest request,
        CancellationToken ct);

    /// <summary>Checks whether a local session is available through the supported SDK runtime.</summary>
    /// <param name="sessionId">Copilot session identifier.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>Explicit availability result.</returns>
    ValueTask<CopilotSessionAvailabilityResult> CheckSessionAvailabilityAsync(
        string sessionId,
        CancellationToken ct);
}
