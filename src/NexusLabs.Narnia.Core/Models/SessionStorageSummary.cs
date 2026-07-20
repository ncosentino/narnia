namespace NexusLabs.Narnia.Core.Models;

/// <summary>Logical-byte totals for the storage categories Narnia measures.</summary>
/// <param name="EventsBytes">Bytes used by the session event log.</param>
/// <param name="SessionDatabaseBytes">Bytes used by the session-specific database.</param>
/// <param name="CheckpointsBytes">Bytes used by checkpoints.</param>
/// <param name="RewindBytes">Bytes used by rewind snapshots.</param>
/// <param name="ArtifactsBytes">Bytes used by session artifacts and research output.</param>
/// <param name="OtherBytes">Bytes not assigned to another category.</param>
public sealed record SessionStorageCategoryTotals(
    long EventsBytes,
    long SessionDatabaseBytes,
    long CheckpointsBytes,
    long RewindBytes,
    long ArtifactsBytes,
    long OtherBytes)
{
    /// <summary>Gets the sum of every category.</summary>
    public long TotalBytes =>
        EventsBytes +
        SessionDatabaseBytes +
        CheckpointsBytes +
        RewindBytes +
        ArtifactsBytes +
        OtherBytes;
}

/// <summary>One daily aggregate captured after a successful session-storage scan.</summary>
/// <param name="SnapshotDate">UTC calendar date represented by the aggregate.</param>
/// <param name="ScannedAt">When the aggregate was captured.</param>
/// <param name="SessionCount">Number of local session-state directories measured.</param>
/// <param name="Categories">Logical-byte totals by category.</param>
public sealed record SessionStorageDailySnapshot(
    DateOnly SnapshotDate,
    DateTimeOffset ScannedAt,
    int SessionCount,
    SessionStorageCategoryTotals Categories);

/// <summary>Persisted outcome of the most recent completed storage scan.</summary>
/// <param name="Status">Completed or failed status.</param>
/// <param name="StartedAt">When the scan began.</param>
/// <param name="CompletedAt">When the scan finished.</param>
/// <param name="SessionCount">Number of session directories observed.</param>
/// <param name="CompleteCount">Number measured without filesystem errors.</param>
/// <param name="Error">Scan-wide failure message, when present.</param>
public sealed record SessionStorageScanInfo(
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    int SessionCount,
    int CompleteCount,
    string? Error);

/// <summary>Describes how a session appears across the Copilot index and local session-state storage.</summary>
public enum SessionStorageDataState
{
    /// <summary>The session exists in the Copilot index and has local state.</summary>
    IndexedWithLocalState,

    /// <summary>A local state directory exists without a corresponding indexed session.</summary>
    LocalStateOnly,

    /// <summary>The indexed session currently has no local state directory.</summary>
    IndexedOnly,
}

/// <summary>Storage and protection information shown for one session.</summary>
public sealed record SessionStorageItem
{
    /// <summary>Gets the Copilot session identifier.</summary>
    public required string SessionId { get; init; }

    /// <summary>Gets the effective session name or summary.</summary>
    public string? Summary { get; init; }

    /// <summary>Gets the effective repository slug.</summary>
    public string? Repository { get; init; }

    /// <summary>Gets when the indexed session was created.</summary>
    public DateTimeOffset? CreatedAt { get; init; }

    /// <summary>Gets when the indexed session was most recently updated.</summary>
    public DateTimeOffset? UpdatedAt { get; init; }

    /// <summary>Gets how local state and indexed history relate for this session.</summary>
    public required SessionStorageDataState DataState { get; init; }

    /// <summary>Gets the cached local storage measurement, when local state exists.</summary>
    public SessionStorageRecord? Storage { get; init; }

    /// <summary>Gets whether a live Copilot process currently owns this session.</summary>
    public required bool IsActive { get; init; }

    /// <summary>Gets whether the session is favorited in Narnia.</summary>
    public required bool IsFavorite { get; init; }

    /// <summary>Gets whether the session is archived in Narnia.</summary>
    public required bool IsArchived { get; init; }

    /// <summary>Gets whether Narnia stores an alias or notes for the session.</summary>
    public required bool HasNarniaMetadata { get; init; }

    /// <summary>Gets whether the session belongs to a Session Group.</summary>
    public required bool IsInSessionGroup { get; init; }

    /// <summary>Gets whether the session belongs to a Collection.</summary>
    public required bool IsInCollection { get; init; }

    /// <summary>Gets default cleanup-protection reasons.</summary>
    public required IReadOnlyList<string> ProtectionReasons { get; init; }

    /// <summary>Gets whether cleanup requires an explicit protection override.</summary>
    public bool IsProtected => ProtectionReasons.Count > 0;
}

/// <summary>Top-level totals for the Session Storage page.</summary>
/// <param name="Categories">Logical-byte totals by category.</param>
/// <param name="LocalStateCount">Number of measured local state directories.</param>
/// <param name="IndexedOnlyCount">Indexed sessions without local state.</param>
/// <param name="LocalStateOnlyCount">Local state directories without indexed sessions.</param>
/// <param name="ActiveCount">Sessions owned by live Copilot processes.</param>
/// <param name="ProtectedCount">Local sessions with default Narnia cleanup protections.</param>
/// <param name="IncompleteCount">Local measurements with filesystem errors.</param>
public sealed record SessionStorageOverview(
    SessionStorageCategoryTotals Categories,
    int LocalStateCount,
    int IndexedOnlyCount,
    int LocalStateOnlyCount,
    int ActiveCount,
    int ProtectedCount,
    int IncompleteCount);

/// <summary>Complete cached data needed to render the Session Storage experience.</summary>
/// <param name="Overview">Top-level storage totals and counts.</param>
/// <param name="Sessions">Enriched session storage rows.</param>
/// <param name="History">Recent global daily snapshots.</param>
/// <param name="CleanupHistory">Recent local cleanup audit entries.</param>
/// <param name="LastScan">Latest persisted scan result.</param>
public sealed record SessionStorageDashboard(
    SessionStorageOverview Overview,
    IReadOnlyList<SessionStorageItem> Sessions,
    IReadOnlyList<SessionStorageDailySnapshot> History,
    IReadOnlyList<SessionCleanupAuditEntry> CleanupHistory,
    SessionStorageScanInfo? LastScan);
