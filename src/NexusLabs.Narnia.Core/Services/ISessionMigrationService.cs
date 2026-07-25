using NexusLabs.Narnia.Core.Models;

namespace NexusLabs.Narnia.Core.Services;

/// <summary>Recovers incompatible sessions in place while preserving folder, identifier, and provenance.</summary>
public interface ISessionMigrationService
{
    /// <summary>Previews recoverable history, compatibility evidence, and metadata transfer.</summary>
    /// <param name="sourceSessionId">Original Copilot session identifier.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>Migration preview.</returns>
    ValueTask<SessionMigrationPreview> PreviewAsync(
        string sourceSessionId,
        CancellationToken ct);

    /// <summary>Archives the broken event stream and asks Copilot to reseed the same session.</summary>
    /// <param name="sourceSessionId">Original Copilot session identifier.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>Migration result with the persistent relationship.</returns>
    ValueTask<SessionMigrationResult> MigrateAsync(
        string sourceSessionId,
        CancellationToken ct);

    /// <summary>Gets the migration related to a source or replacement session.</summary>
    /// <param name="sessionId">Source or replacement Copilot session identifier.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>Related migration, or <c>null</c>.</returns>
    ValueTask<SessionMigration?> GetRelatedAsync(string sessionId, CancellationToken ct);

    /// <summary>Reads a bounded chunk from a Narnia-owned recovery packet.</summary>
    /// <param name="sessionId">Source or replacement Copilot session identifier.</param>
    /// <param name="offset">Zero-based character offset.</param>
    /// <param name="maxCharacters">Maximum characters to return.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>Packet chunk, or <c>null</c> when no completed packet is available.</returns>
    ValueTask<SessionRecoveryPacketChunk?> ReadPacketAsync(
        string sessionId,
        int offset,
        int maxCharacters,
        CancellationToken ct);
}
