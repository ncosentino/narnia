using NexusLabs.Narnia.Core.Models;

namespace NexusLabs.Narnia.Core.Services;

/// <summary>
/// Creates, inspects, and imports versioned file-based scheduled-job packages. The service transfers
/// only portable declarations and always materializes destination jobs through
/// <see cref="IScheduledJobService"/> so wrappers and OS tasks are regenerated locally.
/// </summary>
public interface IScheduledJobPackageService
{
    /// <summary>Exports existing Narnia jobs into one portable package.</summary>
    ValueTask<ScheduledJobPackageExportResult> ExportAsync(
        ScheduledJobPackageExportRequest request,
        CancellationToken ct);

    /// <summary>Builds a package from canonical definitions synthesized from external tasks.</summary>
    ValueTask<ScheduledJobPackageExportResult> BuildAsync(
        ScheduledJobPackageBuildRequest request,
        CancellationToken ct);

    /// <summary>Inspects a package against the destination without changing catalog or scheduler state.</summary>
    ValueTask<ScheduledJobPackagePreviewResult> PreviewAsync(
        ScheduledJobPackagePreviewRequest request,
        CancellationToken ct);

    /// <summary>Imports a current accepted preview as newly generated, disabled destination jobs.</summary>
    ValueTask<ScheduledJobPackageImportResult> ImportAsync(
        ScheduledJobPackageImportRequest request,
        CancellationToken ct);
}
