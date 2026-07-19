using NexusLabs.Narnia.Core.Models;

namespace NexusLabs.Narnia.Core.Services;

/// <summary>Validates and performs supported local Copilot session deletion.</summary>
public interface ISessionCleanupService
{
    /// <summary>Builds a dry-run preview for selected sessions.</summary>
    /// <param name="sessionIds">Selected Copilot session identifiers.</param>
    /// <param name="overrideProtections">Whether default Narnia protections may be overridden.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>Per-session dry-run decisions and aggregate reclaim estimates.</returns>
    ValueTask<SessionCleanupPreview> PreviewAsync(
        IReadOnlyCollection<string> sessionIds,
        bool overrideProtections,
        CancellationToken ct);

    /// <summary>Revalidates and deletes every safe selected local session through Copilot SDK.</summary>
    /// <param name="sessionIds">Selected Copilot session identifiers.</param>
    /// <param name="overrideProtections">Whether default Narnia protections may be overridden.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>Per-session deletion outcomes.</returns>
    ValueTask<SessionCleanupBatchResult> DeleteAsync(
        IReadOnlyCollection<string> sessionIds,
        bool overrideProtections,
        CancellationToken ct);
}
