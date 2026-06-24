using NexusLabs.Narnia.Core.Models;

namespace NexusLabs.Narnia.Core.Services;

/// <summary>
/// Reconstructs the set of currently-open terminal windows of Copilot tabs from a
/// process snapshot. Only windows containing at least one <c>copilot --resume</c> tab
/// are returned; non-Copilot tabs and windows are ignored entirely.
/// </summary>
public interface ILiveWindowDetector
{
    /// <summary>
    /// Detects the live terminal windows visible in the current process snapshot.
    /// </summary>
    /// <returns>Detected windows, each with its Copilot tabs in tab order.</returns>
    IReadOnlyList<DetectedWindow> DetectWindows();
}
