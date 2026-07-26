namespace NexusLabs.Narnia.Core.Models;

/// <summary>How a schedule package is intended to be used.</summary>
public enum ScheduledJobPackageProfile
{
    /// <summary>Retains non-secret source hints that help move work between the owner's machines.</summary>
    Transfer,

    /// <summary>Removes source-local hints so the package is suitable as a reusable template.</summary>
    Share,
}

/// <summary>The kind of target-specific value required by a portable schedule package.</summary>
public enum ScheduledJobPackageBindingKind
{
    /// <summary>A filesystem path whose file/directory role could not be determined.</summary>
    Path,

    /// <summary>A directory path.</summary>
    Directory,

    /// <summary>A file path.</summary>
    File,

    /// <summary>A source repository root.</summary>
    Repository,

    /// <summary>A non-secret string value such as a named profile.</summary>
    String,
}

/// <summary>The kind of runtime dependency declared by a portable job.</summary>
public enum ScheduledJobPackageDependencyKind
{
    /// <summary>A skill supplied by a globally installed Copilot plugin.</summary>
    PluginSkill,

    /// <summary>A skill resolved from the mapped working repository.</summary>
    RepoLocalSkill,

    /// <summary>External configuration that must exist on the destination.</summary>
    Configuration,

    /// <summary>Durable external state that is deliberately not included in the package.</summary>
    ExternalState,
}

/// <summary>Severity assigned to an import-preview finding.</summary>
public enum ScheduledJobPackageFindingSeverity
{
    /// <summary>Informational context that does not prevent import.</summary>
    Info,

    /// <summary>A condition requiring review but not necessarily blocking import.</summary>
    Warning,

    /// <summary>A condition that prevents safe import.</summary>
    Error,
}

/// <summary>Overall readiness of one packaged job during import preview.</summary>
public enum ScheduledJobPackagePreviewStatus
{
    /// <summary>All required values and dependencies are available.</summary>
    Ready,

    /// <summary>One or more required target bindings are missing.</summary>
    NeedsBinding,

    /// <summary>A declared runtime dependency is unavailable.</summary>
    MissingDependency,

    /// <summary>The destination task name is already in use.</summary>
    TaskNameConflict,

    /// <summary>The same portable job has already been imported.</summary>
    AlreadyImported,

    /// <summary>The package is invalid or cannot be rendered safely.</summary>
    Invalid,
}

/// <summary>Metadata about the environment that created a schedule package.</summary>
/// <param name="NarniaVersion">Narnia informational version.</param>
/// <param name="TimeZoneId">Source system time-zone identifier.</param>
public sealed record ScheduledJobPackageSource(
    string NarniaVersion,
    string TimeZoneId);

/// <summary>A target-specific value referenced by one or more portable jobs.</summary>
/// <param name="Id">Stable package-local binding identifier.</param>
/// <param name="Kind">Expected value kind.</param>
/// <param name="Description">Human-readable purpose of the value.</param>
/// <param name="Required">Whether import requires a resolved value.</param>
/// <param name="SourceHint">Original non-secret source value retained only for transfer packages.</param>
/// <param name="RepositoryRemote">Expected repository remote when known.</param>
/// <param name="RelativePath">Path relative to a repository binding when applicable.</param>
public sealed record ScheduledJobPackageBinding(
    string Id,
    ScheduledJobPackageBindingKind Kind,
    string Description,
    bool Required,
    string? SourceHint,
    string? RepositoryRemote,
    string? RelativePath);

/// <summary>A runtime requirement declared by a portable schedule package.</summary>
/// <param name="Id">Stable package-local dependency identifier.</param>
/// <param name="Kind">Dependency kind.</param>
/// <param name="Name">Skill, plugin, profile, or state name.</param>
/// <param name="Required">Whether the dependency is required for the job to run.</param>
/// <param name="PluginName">Installed plugin name when known.</param>
/// <param name="Marketplace">Copilot plugin marketplace when known.</param>
/// <param name="Version">Plugin or dependency version when known.</param>
/// <param name="BindingId">Related path/repository binding when applicable.</param>
/// <param name="RelativePath">Expected path below the related binding.</param>
/// <param name="Description">Additional setup guidance.</param>
public sealed record ScheduledJobPackageDependency(
    string Id,
    ScheduledJobPackageDependencyKind Kind,
    string Name,
    bool Required,
    string? PluginName,
    string? Marketplace,
    string? Version,
    string? BindingId,
    string? RelativePath,
    string? Description);

/// <summary>A skill reference embedded in a portable job definition.</summary>
/// <param name="Skill">Skill name used by the prompt.</param>
/// <param name="Resolution">Where the skill resolves from.</param>
/// <param name="DependencyId">Package dependency describing how the target resolves the skill.</param>
/// <param name="Order">Zero-based execution/documentation order.</param>
public sealed record ScheduledJobPackageSkill(
    string Skill,
    SkillResolution Resolution,
    string DependencyId,
    int Order);

/// <summary>A JSON-friendly normalized schedule cadence.</summary>
/// <param name="Kind">Daily, weekly, or monthly.</param>
/// <param name="Time">Local wall-clock time in <c>HH:mm</c> form.</param>
/// <param name="Days">Weekly day names.</param>
/// <param name="DayOfMonth">Monthly day number.</param>
public sealed record ScheduledJobPackageCadence(
    ScheduleCadenceKind Kind,
    string Time,
    IReadOnlyList<string> Days,
    int DayOfMonth);

/// <summary>The portable behavioral definition stored inside a package.</summary>
/// <param name="Name">User-facing display name.</param>
/// <param name="Description">What the job does.</param>
/// <param name="PromptTemplate">Prompt containing package binding tokens.</param>
/// <param name="WorkingDirectoryBindingId">Binding that resolves the destination working directory.</param>
/// <param name="AllowFlags">Copilot allow flags.</param>
/// <param name="CopilotArgs">Additional Copilot arguments.</param>
/// <param name="TaskName">Preferred destination Task Scheduler name.</param>
/// <param name="Cadence">Normalized cadence.</param>
/// <param name="Skills">Ordered skill references.</param>
public sealed record ScheduledJobPortableDefinition(
    string Name,
    string? Description,
    string PromptTemplate,
    string? WorkingDirectoryBindingId,
    string? AllowFlags,
    string? CopilotArgs,
    string TaskName,
    ScheduledJobPackageCadence Cadence,
    IReadOnlyList<ScheduledJobPackageSkill> Skills);

/// <summary>One portable job in a schedule package.</summary>
/// <param name="PortableJobId">Stable package-lineage identifier; never used as the destination local ID.</param>
/// <param name="DefinitionFingerprint">SHA-256 of the normalized portable definition.</param>
/// <param name="Definition">Portable behavior.</param>
/// <param name="SourceJobId">Source Narnia job ID retained only when applicable.</param>
/// <param name="SourceTaskName">Source Task Scheduler name retained only when applicable.</param>
/// <param name="SourceEnabled">Whether the source task was enabled when exported, if known.</param>
public sealed record ScheduledJobPackageJob(
    string PortableJobId,
    string DefinitionFingerprint,
    ScheduledJobPortableDefinition Definition,
    string? SourceJobId,
    string? SourceTaskName,
    bool? SourceEnabled);

/// <summary>A complete versioned schedule-transfer artifact.</summary>
/// <param name="Format">Fixed package format identifier.</param>
/// <param name="SchemaVersion">Package schema version.</param>
/// <param name="PackageId">Unique package lineage identifier.</param>
/// <param name="Profile">Transfer or share behavior.</param>
/// <param name="CreatedAtUtc">Package creation time.</param>
/// <param name="Source">Source environment metadata.</param>
/// <param name="Bindings">Target-specific values referenced by the jobs.</param>
/// <param name="Dependencies">Declared runtime requirements.</param>
/// <param name="Jobs">Portable job definitions.</param>
public sealed record ScheduledJobPackage(
    string Format,
    int SchemaVersion,
    string PackageId,
    ScheduledJobPackageProfile Profile,
    DateTimeOffset CreatedAtUtc,
    ScheduledJobPackageSource Source,
    IReadOnlyList<ScheduledJobPackageBinding> Bindings,
    IReadOnlyList<ScheduledJobPackageDependency> Dependencies,
    IReadOnlyList<ScheduledJobPackageJob> Jobs);

/// <summary>A caller-supplied destination value for a package binding.</summary>
/// <param name="Id">Binding identifier.</param>
/// <param name="Value">Destination value.</param>
public sealed record ScheduledJobPackageBindingValue(
    string Id,
    string Value);

/// <summary>Per-job destination choices used by preview and import.</summary>
/// <param name="PortableJobId">Portable job identifier.</param>
/// <param name="TaskName">Destination task-name override, or <c>null</c> to retain the packaged name.</param>
/// <param name="AllowDuplicate">Whether an already-imported portable job may be created as another copy.</param>
/// <param name="Skip">Whether this job should be omitted from the current import batch.</param>
public sealed record ScheduledJobPackageJobOptions(
    string PortableJobId,
    string? TaskName,
    bool AllowDuplicate,
    bool Skip);

/// <summary>Request to export existing Narnia jobs.</summary>
/// <param name="JobIds">Local job IDs to export.</param>
/// <param name="Profile">Transfer or share package profile.</param>
public sealed record ScheduledJobPackageExportRequest(
    IReadOnlyList<string> JobIds,
    ScheduledJobPackageProfile Profile);

/// <summary>Request to build a package from canonical definitions synthesized from external tasks.</summary>
/// <param name="Definitions">Definitions to package.</param>
/// <param name="AdditionalDependencies">Configuration or external-state requirements identified during task inspection.</param>
/// <param name="Profile">Transfer or share package profile.</param>
public sealed record ScheduledJobPackageBuildRequest(
    IReadOnlyList<ScheduledJobDefinition> Definitions,
    IReadOnlyList<ScheduledJobPackageDependency> AdditionalDependencies,
    ScheduledJobPackageProfile Profile);

/// <summary>Result of exporting or building a package.</summary>
/// <param name="Ok">Whether package creation succeeded.</param>
/// <param name="Error">Failure reason.</param>
/// <param name="Package">Created package.</param>
/// <param name="PackageJson">Formatted JSON artifact.</param>
/// <param name="Warnings">Non-blocking portability warnings.</param>
public sealed record ScheduledJobPackageExportResult(
    bool Ok,
    string? Error,
    ScheduledJobPackage? Package,
    string? PackageJson,
    IReadOnlyList<string> Warnings);

/// <summary>Request to inspect a package against the destination machine.</summary>
/// <param name="PackageJson">Complete package JSON.</param>
/// <param name="Bindings">Destination binding values.</param>
/// <param name="JobOptions">Per-job task-name and duplicate choices.</param>
public sealed record ScheduledJobPackagePreviewRequest(
    string PackageJson,
    IReadOnlyList<ScheduledJobPackageBindingValue> Bindings,
    IReadOnlyList<ScheduledJobPackageJobOptions> JobOptions);

/// <summary>One preview diagnostic.</summary>
/// <param name="Code">Stable machine-readable code.</param>
/// <param name="Severity">Info, warning, or error.</param>
/// <param name="Message">Human-readable explanation.</param>
/// <param name="BindingId">Related binding when applicable.</param>
public sealed record ScheduledJobPackageFinding(
    string Code,
    ScheduledJobPackageFindingSeverity Severity,
    string Message,
    string? BindingId);

/// <summary>Destination readiness for one binding.</summary>
/// <param name="Id">Binding identifier.</param>
/// <param name="Kind">Expected value kind.</param>
/// <param name="Description">Human-readable purpose.</param>
/// <param name="Required">Whether the binding must resolve.</param>
/// <param name="SourceHint">Transfer-only source hint.</param>
/// <param name="ResolvedValue">Destination value selected by the caller or inferred from an existing source hint.</param>
/// <param name="Resolved">Whether a usable value was found.</param>
/// <param name="Error">Validation failure.</param>
public sealed record ScheduledJobPackageBindingPreview(
    string Id,
    ScheduledJobPackageBindingKind Kind,
    string Description,
    bool Required,
    string? SourceHint,
    string? ResolvedValue,
    bool Resolved,
    string? Error);

/// <summary>Preview of one destination job.</summary>
/// <param name="PortableJobId">Portable job identifier.</param>
/// <param name="Name">Display name.</param>
/// <param name="TargetTaskName">Task name that would be registered.</param>
/// <param name="Status">Overall readiness.</param>
/// <param name="CanImport">Whether import may safely materialize the job.</param>
/// <param name="WillImport">Whether this job is selected for materialization rather than skipped.</param>
/// <param name="RenderedDefinition">Concrete target definition after binding resolution.</param>
/// <param name="Findings">Diagnostics and warnings.</param>
public sealed record ScheduledJobPackageJobPreview(
    string PortableJobId,
    string Name,
    string TargetTaskName,
    ScheduledJobPackagePreviewStatus Status,
    bool CanImport,
    bool WillImport,
    ScheduledJobDefinition? RenderedDefinition,
    IReadOnlyList<ScheduledJobPackageFinding> Findings);

/// <summary>Side-effect-free destination preview for a package.</summary>
/// <param name="Ok">Whether the package itself could be parsed and inspected.</param>
/// <param name="Error">Package-level failure.</param>
/// <param name="PackageId">Package identity.</param>
/// <param name="PackageFingerprint">SHA-256 of the supplied package.</param>
/// <param name="PreviewFingerprint">SHA-256 binding package, choices, and current destination findings.</param>
/// <param name="Bindings">Binding readiness.</param>
/// <param name="Jobs">Per-job readiness.</param>
public sealed record ScheduledJobPackagePreviewResult(
    bool Ok,
    string? Error,
    string? PackageId,
    string? PackageFingerprint,
    string? PreviewFingerprint,
    IReadOnlyList<ScheduledJobPackageBindingPreview> Bindings,
    IReadOnlyList<ScheduledJobPackageJobPreview> Jobs);

/// <summary>Request to import a previously previewed package.</summary>
/// <param name="PackageJson">Complete package JSON.</param>
/// <param name="Bindings">Destination binding values.</param>
/// <param name="JobOptions">Per-job choices.</param>
/// <param name="PreviewFingerprint">Fingerprint returned by the accepted preview.</param>
public sealed record ScheduledJobPackageImportRequest(
    string PackageJson,
    IReadOnlyList<ScheduledJobPackageBindingValue> Bindings,
    IReadOnlyList<ScheduledJobPackageJobOptions> JobOptions,
    string PreviewFingerprint);

/// <summary>Result of materializing one portable job.</summary>
/// <param name="PortableJobId">Portable job identifier.</param>
/// <param name="Ok">Whether import succeeded.</param>
/// <param name="LocalJobId">New destination Narnia job ID.</param>
/// <param name="TaskName">Destination task name.</param>
/// <param name="Error">Failure or rollback detail.</param>
public sealed record ScheduledJobPackageImportedJob(
    string PortableJobId,
    bool Ok,
    string? LocalJobId,
    string TaskName,
    string? Error);

/// <summary>Portable evidence of what a destination imported.</summary>
/// <param name="PackageId">Package identity.</param>
/// <param name="PackageFingerprint">Imported package fingerprint.</param>
/// <param name="ImportedAtUtc">Import completion time.</param>
/// <param name="Jobs">Portable-to-local job mapping.</param>
public sealed record ScheduledJobPackageImportReceipt(
    string PackageId,
    string PackageFingerprint,
    DateTimeOffset ImportedAtUtc,
    IReadOnlyList<ScheduledJobPackageImportedJob> Jobs);

/// <summary>Result of importing a package.</summary>
/// <param name="Ok">Whether every requested job imported successfully.</param>
/// <param name="Error">Batch-level failure.</param>
/// <param name="Jobs">Per-job results, including rollback details.</param>
/// <param name="Receipt">Receipt produced only after successful import.</param>
public sealed record ScheduledJobPackageImportResult(
    bool Ok,
    string? Error,
    IReadOnlyList<ScheduledJobPackageImportedJob> Jobs,
    ScheduledJobPackageImportReceipt? Receipt);

/// <summary>Persisted provenance linking an imported definition to its local Narnia job.</summary>
/// <param name="JobId">Destination Narnia job ID.</param>
/// <param name="PackageId">Source package identity.</param>
/// <param name="PortableJobId">Portable job identity.</param>
/// <param name="DefinitionFingerprint">Definition fingerprint at import.</param>
/// <param name="SourceJobId">Original Narnia job ID when supplied.</param>
/// <param name="ImportedAt">Import timestamp.</param>
public sealed record ScheduledJobImportRecord(
    string JobId,
    string PackageId,
    string PortableJobId,
    string DefinitionFingerprint,
    string? SourceJobId,
    DateTimeOffset ImportedAt);
