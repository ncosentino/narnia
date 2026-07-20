using NexusLabs.Narnia.Core.Models;

namespace NexusLabs.Narnia.Core.Services;

/// <summary>Checks session-contained Git repositories for cleanup risks.</summary>
public interface IGitArtifactInspector
{
    /// <summary>Inspects Git markers beneath a selected session immediately before deletion.</summary>
    /// <param name="sessionDirectory">Resolved local session-state directory.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>Whether Git state is safe and any blocking reasons.</returns>
    ValueTask<GitArtifactInspection> InspectAsync(
        string sessionDirectory,
        CancellationToken ct);
}
