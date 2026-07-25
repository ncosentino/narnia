namespace NexusLabs.Narnia.Core.Services;

/// <summary>Serializes destructive or state-transitioning operations for the same sessions.</summary>
public interface ISessionOperationCoordinator
{
    /// <summary>Acquires session locks in deterministic order until the returned lease is disposed.</summary>
    /// <param name="sessionIds">Session identifiers whose operations must not overlap.</param>
    /// <param name="ct">Cancellation token while waiting for locks.</param>
    /// <returns>An asynchronous lease that releases every acquired lock.</returns>
    ValueTask<IAsyncDisposable> AcquireAsync(
        IReadOnlyCollection<string> sessionIds,
        CancellationToken ct);

    /// <summary>Attempts to acquire one session lock without waiting.</summary>
    /// <param name="sessionId">Session identifier to protect.</param>
    /// <returns>A lease when acquired; otherwise <c>null</c>.</returns>
    IDisposable? TryAcquire(string sessionId);
}
