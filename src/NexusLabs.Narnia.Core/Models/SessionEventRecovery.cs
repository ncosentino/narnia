namespace NexusLabs.Narnia.Core.Models;

/// <summary>Result of atomically archiving a broken session event stream.</summary>
/// <param name="Archived">Whether the original event stream was moved successfully.</param>
/// <param name="ArchivePath">Absolute archived-event path inside the same session folder.</param>
/// <param name="Sha256">SHA-256 hash of the archived original.</param>
/// <param name="Error">Archival failure detail.</param>
public sealed record SessionEventArchiveResult(
    bool Archived,
    string? ArchivePath,
    string? Sha256,
    string? Error);

/// <summary>Read-only plan for archiving one broken event stream.</summary>
/// <param name="Planned">Whether the source and deterministic archive path passed validation.</param>
/// <param name="ArchivePath">Planned absolute archive path inside the same session folder.</param>
/// <param name="Sha256">SHA-256 hash of the current event stream.</param>
/// <param name="Error">Planning failure detail.</param>
public sealed record SessionEventArchivePlanResult(
    bool Planned,
    string? ArchivePath,
    string? Sha256,
    string? Error);

/// <summary>Result of restoring an archived event stream after failed in-place recovery.</summary>
/// <param name="Restored">Whether the original event stream was restored successfully.</param>
/// <param name="FailedRecoveryPath">Archived replacement event stream, when one existed.</param>
/// <param name="Error">Restore failure detail.</param>
public sealed record SessionEventRestoreResult(
    bool Restored,
    string? FailedRecoveryPath,
    string? Error);
