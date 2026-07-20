namespace NexusLabs.Narnia.Core.Models;

/// <summary>Cached logical disk usage for one local Copilot session-state directory.</summary>
public sealed record SessionStorageRecord
{
    /// <summary>Gets the Copilot session identifier.</summary>
    public required string SessionId { get; init; }

    /// <summary>Gets when the filesystem measurement was captured.</summary>
    public required DateTimeOffset ScannedAt { get; init; }

    /// <summary>Gets the preceding successful measurement time, when one exists.</summary>
    public DateTimeOffset? PreviousScannedAt { get; init; }

    /// <summary>Gets the total logical bytes beneath the session-state directory.</summary>
    public required long TotalBytes { get; init; }

    /// <summary>Gets the total logical bytes from the preceding scan, when one exists.</summary>
    public long? PreviousTotalBytes { get; init; }

    /// <summary>Gets the number of regular files included in the measurement.</summary>
    public required long FileCount { get; init; }

    /// <summary>Gets the most recent file write time observed during the scan.</summary>
    public DateTimeOffset? LastWriteAt { get; init; }

    /// <summary>Gets logical bytes used by the session event log.</summary>
    public required long EventsBytes { get; init; }

    /// <summary>Gets logical bytes used by the session-specific database.</summary>
    public required long SessionDatabaseBytes { get; init; }

    /// <summary>Gets logical bytes used by checkpoints.</summary>
    public required long CheckpointsBytes { get; init; }

    /// <summary>Gets logical bytes used by rewind snapshots.</summary>
    public required long RewindBytes { get; init; }

    /// <summary>Gets logical bytes used by session artifacts and research output.</summary>
    public required long ArtifactsBytes { get; init; }

    /// <summary>Gets logical bytes not assigned to another storage category.</summary>
    public required long OtherBytes { get; init; }

    /// <summary>Gets the size of the largest regular file observed.</summary>
    public required long LargestFileBytes { get; init; }

    /// <summary>Gets the largest file path relative to the session-state directory.</summary>
    public string? LargestFilePath { get; init; }

    /// <summary>Gets whether every accessible non-reparse entry was measured successfully.</summary>
    public required bool IsComplete { get; init; }

    /// <summary>Gets the scan error when the measurement is incomplete.</summary>
    public string? Error { get; init; }

    /// <summary>Gets whether Copilot recorded that the user explicitly named the session.</summary>
    public required bool IsUserNamed { get; init; }

    /// <summary>Gets whether a standalone Git repository marker was found in session artifacts.</summary>
    public required bool ContainsGitRepository { get; init; }

    /// <summary>Gets whether a linked Git worktree marker was found in session artifacts.</summary>
    public required bool ContainsLinkedWorktree { get; init; }

    /// <summary>Gets whether a filesystem reparse point was skipped.</summary>
    public required bool ContainsReparsePoint { get; init; }

    /// <summary>Gets the logical-byte change since the preceding scan.</summary>
    public long GrowthBytes => PreviousTotalBytes is null ? 0 : TotalBytes - PreviousTotalBytes.Value;
}
