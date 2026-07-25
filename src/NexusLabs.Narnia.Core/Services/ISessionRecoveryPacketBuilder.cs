using NexusLabs.Narnia.Core.Models;

namespace NexusLabs.Narnia.Core.Services;

/// <summary>Builds bounded, Narnia-owned recovery context from read-only session history.</summary>
public interface ISessionRecoveryPacketBuilder
{
    /// <summary>Builds the archival packet and bootstrap prompt for a successor session.</summary>
    /// <param name="sourceSessionId">Original Copilot session identifier.</param>
    /// <param name="replacementSessionId">Planned successor identifier.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>Explicit packet-generation result.</returns>
    ValueTask<SessionRecoveryPacketBuildResult> BuildAsync(
        string sourceSessionId,
        string replacementSessionId,
        CancellationToken ct);
}
