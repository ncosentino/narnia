using NexusLabs.Narnia.Core.Models;

namespace NexusLabs.Narnia.Core.Repositories;

/// <summary>
/// Captures platform-specific process resource data for live diagnostics.
/// </summary>
public interface IProcessResourceSnapshotProvider
{
    /// <summary>
    /// Gets whether the current platform supports this provider.
    /// </summary>
    bool IsSupported { get; }

    /// <summary>
    /// Captures one process-resource sample.
    /// </summary>
    /// <param name="ct">Cancellation token checked while enumerating processes.</param>
    /// <returns>A usable sample or an explicit unavailable result.</returns>
    ProcessResourceSnapshot Capture(CancellationToken ct = default);
}
