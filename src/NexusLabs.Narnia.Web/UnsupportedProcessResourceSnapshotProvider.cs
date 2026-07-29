using System.Diagnostics;
using NexusLabs.Narnia.Core.Models;
using NexusLabs.Narnia.Core.Repositories;

namespace NexusLabs.Narnia.Web;

/// <summary>
/// Reports that live process diagnostics are unavailable on non-Windows platforms.
/// </summary>
public sealed class UnsupportedProcessResourceSnapshotProvider : IProcessResourceSnapshotProvider
{
    /// <inheritdoc />
    public bool IsSupported => false;

    /// <inheritdoc />
    public ProcessResourceSnapshot Capture(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return new ProcessResourceSnapshot(
            false,
            "Live process diagnostics currently require Windows and WMI.",
            DateTimeOffset.UtcNow,
            Stopwatch.GetElapsedTime(0, Stopwatch.GetTimestamp()),
            Math.Max(1, Environment.ProcessorCount),
            []);
    }
}
