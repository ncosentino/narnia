namespace NexusLabs.Narnia.Core.Models;

/// <summary>How cleanup validation classified a selected session.</summary>
public enum SessionCleanupDisposition
{
    /// <summary>The session passed every cleanup validation.</summary>
    Allowed,

    /// <summary>The session is protected but may be included through an explicit override.</summary>
    Protected,

    /// <summary>The session cannot be deleted safely.</summary>
    Blocked,
}

/// <summary>Cleanup validation for one selected session.</summary>
/// <param name="SessionId">Copilot session identifier.</param>
/// <param name="Summary">Effective session name or summary.</param>
/// <param name="EstimatedBytes">Cached logical bytes expected to be removed.</param>
/// <param name="Disposition">Validation result.</param>
/// <param name="Reasons">Protection or safety reasons associated with the result.</param>
public sealed record SessionCleanupDecision(
    string SessionId,
    string? Summary,
    long EstimatedBytes,
    SessionCleanupDisposition Disposition,
    IReadOnlyList<string> Reasons);

/// <summary>Dry-run cleanup result for a selected set of sessions.</summary>
/// <param name="Decisions">Per-session validation decisions.</param>
/// <param name="AllowedCount">Sessions eligible for deletion.</param>
/// <param name="AllowedBytes">Estimated bytes eligible for deletion.</param>
/// <param name="ProtectedCount">Sessions excluded by default protections.</param>
/// <param name="ProtectedBytes">Estimated bytes excluded by default protections.</param>
/// <param name="BlockedCount">Sessions hard-blocked by safety checks.</param>
public sealed record SessionCleanupPreview(
    IReadOnlyList<SessionCleanupDecision> Decisions,
    int AllowedCount,
    long AllowedBytes,
    int ProtectedCount,
    long ProtectedBytes,
    int BlockedCount);

/// <summary>Outcome returned by the supported Copilot session deletion interface.</summary>
/// <param name="SessionId">Copilot session identifier.</param>
/// <param name="Deleted">Whether local deletion completed successfully.</param>
/// <param name="Error">Deletion error when unsuccessful.</param>
public sealed record CopilotSessionDeletionResult(
    string SessionId,
    bool Deleted,
    string? Error);

/// <summary>Final deletion outcome for one selected session.</summary>
/// <param name="SessionId">Copilot session identifier.</param>
/// <param name="Deleted">Whether local deletion completed successfully.</param>
/// <param name="EstimatedBytes">Cached logical bytes associated with the session.</param>
/// <param name="Reasons">Protection or safety information from final validation.</param>
/// <param name="Error">Deletion error when unsuccessful.</param>
public sealed record SessionCleanupResult(
    string SessionId,
    bool Deleted,
    long EstimatedBytes,
    IReadOnlyList<string> Reasons,
    string? Error);

/// <summary>Batch result after Narnia attempts supported local-session deletion.</summary>
/// <param name="Results">Per-session cleanup outcomes.</param>
public sealed record SessionCleanupBatchResult(IReadOnlyList<SessionCleanupResult> Results)
{
    /// <summary>Gets the number of sessions deleted successfully.</summary>
    public int DeletedCount => Results.Count(result => result.Deleted);

    /// <summary>Gets the estimated logical bytes removed successfully.</summary>
    public long DeletedBytes => Results.Where(result => result.Deleted).Sum(result => result.EstimatedBytes);
}

/// <summary>Persistent Narnia-owned audit entry for one cleanup attempt.</summary>
/// <param name="Id">Narnia-assigned audit identifier.</param>
/// <param name="SessionId">Copilot session identifier.</param>
/// <param name="RequestedAt">When the cleanup request began.</param>
/// <param name="CompletedAt">When the outcome was known.</param>
/// <param name="EstimatedBytes">Cached logical bytes associated with the session.</param>
/// <param name="Result">Deleted, failed, protected, or blocked outcome.</param>
/// <param name="Error">Failure detail when present.</param>
public sealed record SessionCleanupAuditEntry(
    string Id,
    string SessionId,
    DateTimeOffset RequestedAt,
    DateTimeOffset CompletedAt,
    long EstimatedBytes,
    string Result,
    string? Error);

/// <summary>Git-related safety result for artifacts stored beneath one session.</summary>
/// <param name="IsSafe">Whether Git state permits local session deletion.</param>
/// <param name="Reasons">Blocking Git, worktree, or reparse-point reasons.</param>
public sealed record GitArtifactInspection(
    bool IsSafe,
    IReadOnlyList<string> Reasons);
