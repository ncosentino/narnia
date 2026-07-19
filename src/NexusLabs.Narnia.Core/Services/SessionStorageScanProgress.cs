namespace NexusLabs.Narnia.Core.Services;

/// <summary>Current in-memory progress of the background session-storage scanner.</summary>
/// <param name="Status">Idle, running, completed, or failed state.</param>
/// <param name="StartedAt">When the active or latest scan began.</param>
/// <param name="CompletedAt">When the latest scan completed.</param>
/// <param name="ScannedSessions">Number of directories measured so far.</param>
/// <param name="TotalSessions">Total directories in the active scan.</param>
/// <param name="Error">Scan error when the status is failed.</param>
public sealed record SessionStorageScanProgress(
    string Status,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    int ScannedSessions,
    int TotalSessions,
    string? Error);
