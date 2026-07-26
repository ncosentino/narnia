using System.IO.Abstractions;
using NexusLabs.Narnia.Core.Configuration;
using NexusLabs.Narnia.Core.Models;
using NexusLabs.Narnia.Core.Repositories;

namespace NexusLabs.Narnia.Core.Services;

/// <summary>
/// Default <see cref="IScheduledJobPackageService"/> orchestration facade. Package construction,
/// preview analysis, dependency inspection, validation, and import rollback are delegated to
/// focused internal collaborators while callers retain one stable service boundary.
/// </summary>
public sealed class ScheduledJobPackageService : IScheduledJobPackageService
{
    /// <summary>The fixed identifier written into every supported package.</summary>
    public const string PackageFormat = ScheduledJobPackageFormat.Format;

    /// <summary>The package schema version supported by this build.</summary>
    public const int CurrentSchemaVersion = ScheduledJobPackageFormat.SchemaVersion;

    private readonly IScheduledJobService _jobService;
    private readonly ScheduledJobPackageBuilder _builder;
    private readonly ScheduledJobPackagePreviewer _previewer;
    private readonly ScheduledJobPackageImporter _importer;

    /// <summary>Initializes the package facade and its focused collaborators.</summary>
    /// <param name="jobService">Scheduled-job catalog and registration service.</param>
    /// <param name="importRepository">Narnia-owned package-import provenance repository.</param>
    /// <param name="options">Narnia filesystem and Copilot plugin paths.</param>
    /// <param name="fileSystem">Filesystem abstraction used for portability inspection.</param>
    public ScheduledJobPackageService(
        IScheduledJobService jobService,
        IScheduledJobImportRepository importRepository,
        NarniaOptions options,
        IFileSystem fileSystem)
    {
        _jobService = jobService;
        var dependencyInspector = new ScheduledJobPackageDependencyInspector(options, fileSystem);
        var definitionRenderer = new ScheduledJobPackageDefinitionRenderer(fileSystem);
        _builder = new ScheduledJobPackageBuilder(fileSystem, dependencyInspector);
        _previewer = new ScheduledJobPackagePreviewer(
            jobService,
            importRepository,
            definitionRenderer,
            dependencyInspector);
        _importer = new ScheduledJobPackageImporter(
            jobService,
            importRepository,
            _previewer);
    }

    /// <inheritdoc />
    public async ValueTask<ScheduledJobPackageExportResult> ExportAsync(
        ScheduledJobPackageExportRequest request,
        CancellationToken ct)
    {
        var ids = request.JobIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (ids.Length == 0)
            return Failure("Select at least one scheduled job to export.");
        if (ids.Length > ScheduledJobPackageFormat.MaxJobs)
        {
            return Failure(
                $"A package may contain at most {ScheduledJobPackageFormat.MaxJobs} jobs.");
        }

        var list = await _jobService.ListAsync(ct);
        var jobsById = list.Jobs.ToDictionary(view => view.Job.Id, StringComparer.Ordinal);
        var sources = new List<ScheduledJobPackageSourceJob>(ids.Length);
        foreach (var id in ids)
        {
            if (!jobsById.TryGetValue(id, out var view))
                return Failure($"Scheduled job '{id}' was not found.");

            var definition = ScheduledJobDefinitions.FromJob(view.Job);
            if (definition.Error is not null)
                return Failure(definition.Error);

            sources.Add(new ScheduledJobPackageSourceJob(
                definition.Definition!,
                view.Job.Id,
                view.Job.TaskName,
                view.Status is null
                    ? null
                    : view.Status.State != ScheduledTaskState.Disabled,
                view.Job.Id));
        }

        return _builder.Build(
            sources,
            request.Profile,
            ScheduledJobPackageFormat.StablePackageId(ids, request.Profile),
            []);
    }

    /// <inheritdoc />
    public ValueTask<ScheduledJobPackageExportResult> BuildAsync(
        ScheduledJobPackageBuildRequest request,
        CancellationToken ct)
    {
        _ = ct;
        if (request.Definitions.Count == 0)
        {
            return ValueTask.FromResult(
                Failure("Provide at least one scheduled job definition."));
        }
        if (request.Definitions.Count > ScheduledJobPackageFormat.MaxJobs)
        {
            return ValueTask.FromResult(
                Failure($"A package may contain at most {ScheduledJobPackageFormat.MaxJobs} jobs."));
        }

        var sources = request.Definitions
            .Select(definition => new ScheduledJobPackageSourceJob(
                definition,
                null,
                null,
                null,
                Guid.NewGuid().ToString()))
            .ToArray();
        return ValueTask.FromResult(_builder.Build(
            sources,
            request.Profile,
            Guid.NewGuid().ToString(),
            request.AdditionalDependencies));
    }

    /// <inheritdoc />
    public ValueTask<ScheduledJobPackagePreviewResult> PreviewAsync(
        ScheduledJobPackagePreviewRequest request,
        CancellationToken ct) =>
        _previewer.PreviewAsync(request, ct);

    /// <inheritdoc />
    public ValueTask<ScheduledJobPackageImportResult> ImportAsync(
        ScheduledJobPackageImportRequest request,
        CancellationToken ct) =>
        _importer.ImportAsync(request, ct);

    private static ScheduledJobPackageExportResult Failure(string error) =>
        new(false, error, null, null, []);
}
