using NexusLabs.Narnia.Core.Models;
using NexusLabs.Narnia.Core.Repositories;

namespace NexusLabs.Narnia.Core.Services;

internal sealed class ScheduledJobPackageImporter(
    IScheduledJobService jobService,
    IScheduledJobImportRepository importRepository,
    ScheduledJobPackagePreviewer previewer)
{
    public async ValueTask<ScheduledJobPackageImportResult> ImportAsync(
        ScheduledJobPackageImportRequest request,
        CancellationToken ct)
    {
        var preview = await previewer.PreviewAsync(
            new ScheduledJobPackagePreviewRequest(
                request.PackageJson,
                request.Bindings,
                request.JobOptions),
            ct);
        if (!preview.Ok)
            return Failure(preview.Error ?? "Package preview failed.");
        if (!string.Equals(
                preview.PreviewFingerprint,
                request.PreviewFingerprint,
                StringComparison.Ordinal))
        {
            return Failure("The package preview is stale. Preview the package again before importing.");
        }
        if (preview.Jobs.Any(job => !job.CanImport))
            return Failure("One or more packaged jobs still have blocking preview findings.");

        var selectedJobs = preview.Jobs
            .Where(job => job.WillImport)
            .ToArray();
        if (selectedJobs.Length == 0)
            return Failure("Select at least one packaged job to import.");
        if (selectedJobs.Any(job => job.RenderedDefinition is null))
            return Failure("One or more selected jobs could not be rendered.");

        var parsed = ScheduledJobPackageFormat.ParseAndValidate(request.PackageJson);
        if (parsed.Error is not null)
            return Failure(parsed.Error);
        var package = parsed.Package!;

        var imported = new List<ScheduledJobPackageImportedJob>();
        var created = new List<(ScheduledJobPackageJob PackageJob, ScheduledJob LocalJob)>();
        foreach (var jobPreview in selectedJobs)
        {
            var packagedJob = package.Jobs.Single(job =>
                job.PortableJobId == jobPreview.PortableJobId);
            var create = await jobService.CreateDisabledAsync(
                ScheduledJobDefinitions.ToInput(jobPreview.RenderedDefinition!),
                ct);
            if (!create.Ok || create.Job is null)
            {
                imported.Add(new ScheduledJobPackageImportedJob(
                    jobPreview.PortableJobId,
                    false,
                    null,
                    jobPreview.TargetTaskName,
                    create.Error ?? "The destination job could not be created."));
                return await RollBackAsync(
                    imported,
                    created,
                    "Package import failed; previously created jobs were rolled back.",
                    ct);
            }

            var localJob = create.Job;
            var record = new ScheduledJobImportRecord(
                localJob.Id,
                package.PackageId,
                packagedJob.PortableJobId,
                packagedJob.DefinitionFingerprint,
                packagedJob.SourceJobId,
                DateTimeOffset.UtcNow);
            try
            {
                await importRepository.AddAsync(record, ct);
            }
            catch (Microsoft.Data.Sqlite.SqliteException ex)
            {
                imported.Add(new ScheduledJobPackageImportedJob(
                    packagedJob.PortableJobId,
                    false,
                    localJob.Id,
                    localJob.TaskName,
                    $"Import provenance could not be stored: {ex.Message}"));
                created.Add((packagedJob, localJob));
                return await RollBackAsync(
                    imported,
                    created,
                    "Package import failed while recording provenance.",
                    ct);
            }

            created.Add((packagedJob, localJob));
            imported.Add(new ScheduledJobPackageImportedJob(
                packagedJob.PortableJobId,
                true,
                localJob.Id,
                localJob.TaskName,
                null));
        }

        var receipt = new ScheduledJobPackageImportReceipt(
            package.PackageId,
            preview.PackageFingerprint!,
            DateTimeOffset.UtcNow,
            imported);
        return new ScheduledJobPackageImportResult(true, null, imported, receipt);
    }

    private async ValueTask<ScheduledJobPackageImportResult> RollBackAsync(
        IReadOnlyList<ScheduledJobPackageImportedJob> currentResults,
        IReadOnlyList<(ScheduledJobPackageJob PackageJob, ScheduledJob LocalJob)> created,
        string error,
        CancellationToken ct)
    {
        var results = currentResults.ToList();
        var cleanupFailures = new List<string>();
        foreach (var item in created.Reverse())
        {
            var deleted = await jobService.DeleteAsync(item.LocalJob.Id, ct);
            if (!deleted.Ok)
            {
                cleanupFailures.Add($"{item.LocalJob.Id}: {deleted.Error}");
                continue;
            }

            try
            {
                await importRepository.DeleteAsync(item.LocalJob.Id, ct);
            }
            catch (Microsoft.Data.Sqlite.SqliteException ex)
            {
                cleanupFailures.Add($"{item.LocalJob.Id} provenance: {ex.Message}");
            }

            var index = results.FindIndex(result => result.LocalJobId == item.LocalJob.Id);
            if (index >= 0)
            {
                results[index] = results[index] with
                {
                    Ok = false,
                    Error = "Rolled back because another job in the package failed.",
                };
            }
        }

        var fullError = cleanupFailures.Count == 0
            ? error
            : $"{error} Cleanup is required for: {string.Join("; ", cleanupFailures)}";
        return new ScheduledJobPackageImportResult(false, fullError, results, null);
    }

    private static ScheduledJobPackageImportResult Failure(string error) =>
        new(false, error, [], null);
}
