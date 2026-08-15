namespace NexusLabs.Narnia.Core.Models;

/// <summary>How safely Narnia expects Copilot to resume a local session.</summary>
public enum SessionResumeSafety
{
    /// <summary>Narnia cannot prove whether the session is resumable.</summary>
    Unknown,

    /// <summary>The persisted event stream begins with the required session-start event.</summary>
    Resumable,

    /// <summary>The persisted event stream is known to be incompatible with Copilot resume.</summary>
    Incompatible,
}

/// <summary>Read-only assessment of a Copilot session's persisted resume contract.</summary>
/// <param name="SessionId">Copilot session identifier.</param>
/// <param name="Safety">Detected resume safety.</param>
/// <param name="Reason">Human-readable evidence for the assessment.</param>
/// <param name="FirstEventType">First persisted event type, when readable.</param>
/// <param name="IsNestedAgent">Whether Copilot recorded the session as a nested agent.</param>
public sealed record SessionResumeAssessment(
    string SessionId,
    SessionResumeSafety Safety,
    string? Reason,
    string? FirstEventType,
    bool IsNestedAgent);

/// <summary>Lifecycle state for a Narnia-owned session migration.</summary>
public enum SessionMigrationStatus
{
    /// <summary>Narnia is preparing the recovery packet or creating the successor.</summary>
    Preparing,

    /// <summary>Copilot reseeded the target session; final Narnia verification is pending.</summary>
    SessionCreated,

    /// <summary>An incomplete successor may still exist and must be removed before retry.</summary>
    CleanupRequired,

    /// <summary>The successor and all Narnia references were migrated successfully.</summary>
    Completed,

    /// <summary>The migration stopped with an explicit failure.</summary>
    Failed,
}

/// <summary>Persistent record of a broken-session recovery attempt.</summary>
/// <param name="Id">Narnia migration identifier.</param>
/// <param name="SourceSessionId">Original Copilot session identifier.</param>
/// <param name="ReplacementSessionId">Recovered identifier; equal to the source for in-place recovery.</param>
/// <param name="Status">Current migration lifecycle state.</param>
/// <param name="RecoveryPacketPath">Narnia-owned recovery packet path.</param>
/// <param name="RecoveryPacketBytes">UTF-8 packet size in bytes.</param>
/// <param name="RecoveryPacketTruncated">Whether bounded packet generation omitted older content.</param>
/// <param name="Error">Most recent migration failure, when present.</param>
/// <param name="CreatedAt">When migration preparation began.</param>
/// <param name="UpdatedAt">When the migration record last changed.</param>
/// <param name="CompletedAt">When the migration completed successfully.</param>
public sealed record SessionMigration(
    string Id,
    string SourceSessionId,
    string ReplacementSessionId,
    SessionMigrationStatus Status,
    string RecoveryPacketPath,
    long RecoveryPacketBytes,
    bool RecoveryPacketTruncated,
    string? Error,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? CompletedAt)
{
    /// <summary>Gets the archived pre-recovery event stream retained in the session folder.</summary>
    public string? ArchivedEventsPath { get; init; }

    /// <summary>Gets the SHA-256 hash recorded before the original event stream was archived.</summary>
    public string? ArchivedEventsSha256 { get; init; }

    /// <summary>Gets the Chronicle turn count recorded before recovery began.</summary>
    public int BaselineTurnCount { get; init; }

    /// <summary>Gets the Chronicle update timestamp recorded before recovery began.</summary>
    public DateTimeOffset? BaselineUpdatedAt { get; init; }

    /// <summary>Gets whether recovery retained the original session identifier and folder.</summary>
    public bool IsInPlace => string.Equals(
        SourceSessionId,
        ReplacementSessionId,
        StringComparison.OrdinalIgnoreCase);
}

/// <summary>Narnia references that will be carried forward to a recovered successor.</summary>
/// <param name="IsFavorite">Whether the source is favorited.</param>
/// <param name="HasAlias">Whether the source has a Narnia alias.</param>
/// <param name="HasNotes">Whether the source has Narnia notes.</param>
/// <param name="CollectionCount">Collections that will also include the successor.</param>
/// <param name="SessionGroupCount">Legacy compatibility field; current Narnia always reports zero.</param>
/// <param name="SavedWindowCount">Saved windows whose source tab will be replaced.</param>
public sealed record SessionMigrationReferenceSummary(
    bool IsFavorite,
    bool HasAlias,
    bool HasNotes,
    int CollectionCount,
    int SessionGroupCount,
    int SavedWindowCount);

/// <summary>Dry-run information for creating a recovered successor session.</summary>
/// <param name="SourceSessionId">Original Copilot session identifier.</param>
/// <param name="Summary">Effective source-session name.</param>
/// <param name="ResumeAssessment">Read-only compatibility evidence.</param>
/// <param name="IsActive">Whether a live Copilot process currently owns the source.</param>
/// <param name="TurnCount">Indexed conversation turns available for recovery.</param>
/// <param name="CheckpointCount">Indexed checkpoints available for recovery.</param>
/// <param name="TodoCount">Workspace tasks available for recovery.</param>
/// <param name="References">Narnia metadata and launch references that will be carried forward.</param>
/// <param name="ExistingMigration">Latest recorded migration for the source, when present.</param>
/// <param name="BlockingReason">Reason migration cannot begin, when blocked.</param>
public sealed record SessionMigrationPreview(
    string SourceSessionId,
    string? Summary,
    SessionResumeAssessment ResumeAssessment,
    bool IsActive,
    int TurnCount,
    int CheckpointCount,
    int TodoCount,
    SessionMigrationReferenceSummary References,
    SessionMigration? ExistingMigration,
    string? BlockingReason)
{
    /// <summary>Gets whether the migration can begin.</summary>
    public bool CanMigrate => string.IsNullOrWhiteSpace(BlockingReason);
}

/// <summary>Result of a requested recovered-session migration.</summary>
/// <param name="Migrated">Whether the successor and Narnia metadata completed successfully.</param>
/// <param name="Migration">Persistent migration record, when one was created.</param>
/// <param name="Error">Failure detail when migration did not complete.</param>
public sealed record SessionMigrationResult(
    bool Migrated,
    SessionMigration? Migration,
    string? Error);

/// <summary>Result of building a Narnia-owned recovery packet and bootstrap prompt.</summary>
/// <param name="Succeeded">Whether packet generation completed.</param>
/// <param name="PacketPath">Absolute Narnia-owned packet path.</param>
/// <param name="PacketBytes">UTF-8 packet size.</param>
/// <param name="PacketTruncated">Whether bounded generation omitted older content.</param>
/// <param name="BootstrapPrompt">Bounded context used to seed the successor session.</param>
/// <param name="Error">Packet-generation failure when unsuccessful.</param>
public sealed record SessionRecoveryPacketBuildResult(
    bool Succeeded,
    string? PacketPath,
    long PacketBytes,
    bool PacketTruncated,
    string? BootstrapPrompt,
    string? Error);

/// <summary>Bounded text chunk read from a persisted recovery packet.</summary>
/// <param name="Content">Requested recovery text.</param>
/// <param name="Offset">Character offset used for this chunk.</param>
/// <param name="NextOffset">Next character offset, or <c>null</c> at end of packet.</param>
/// <param name="TotalCharacters">Total packet character count.</param>
public sealed record SessionRecoveryPacketChunk(
    string Content,
    int Offset,
    int? NextOffset,
    int TotalCharacters);

/// <summary>Request to create and seed a supported Copilot session event stream.</summary>
/// <param name="SessionId">Copilot session identifier to create or reseed.</param>
/// <param name="WorkingDirectory">Working directory inherited from the source session.</param>
/// <param name="BootstrapPrompt">Bounded recovery context sent as the first user message.</param>
public sealed record CopilotRecoverySessionRequest(
    string SessionId,
    string? WorkingDirectory,
    string BootstrapPrompt);

/// <summary>Outcome returned by the supported Copilot session-creation interface.</summary>
/// <param name="SessionId">Requested session identifier.</param>
/// <param name="Created">Whether Copilot created and seeded the session.</param>
/// <param name="Error">Creation or bootstrap failure.</param>
public sealed record CopilotRecoverySessionResult(
    string SessionId,
    bool Created,
    string? Error);

/// <summary>Result of checking whether a Copilot session is available through the supported runtime.</summary>
/// <param name="SessionId">Requested Copilot session identifier.</param>
/// <param name="Checked">Whether the SDK runtime completed the availability check.</param>
/// <param name="Exists">Whether the session is currently available.</param>
/// <param name="Error">Availability-check failure.</param>
public sealed record CopilotSessionAvailabilityResult(
    string SessionId,
    bool Checked,
    bool Exists,
    string? Error);
