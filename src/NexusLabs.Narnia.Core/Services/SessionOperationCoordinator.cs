using System.Collections.Concurrent;

namespace NexusLabs.Narnia.Core.Services;

/// <summary>Provides process-local, case-insensitive session locks for cleanup and migration.</summary>
public sealed class SessionOperationCoordinator : ISessionOperationCoordinator
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks =
        new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public async ValueTask<IAsyncDisposable> AcquireAsync(
        IReadOnlyCollection<string> sessionIds,
        CancellationToken ct)
    {
        var normalized = sessionIds
            .Where(sessionId => !string.IsNullOrWhiteSpace(sessionId))
            .Select(sessionId => sessionId.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(sessionId => sessionId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (normalized.Length == 0)
            return EmptyLease.Instance;

        var acquired = new List<SemaphoreSlim>(normalized.Length);
        try
        {
            foreach (var sessionId in normalized)
            {
                var gate = _locks.GetOrAdd(sessionId, static _ => new SemaphoreSlim(1, 1));
                await gate.WaitAsync(ct);
                acquired.Add(gate);
            }

            return new SessionOperationLease(acquired);
        }
        catch (Exception exception) when (
            exception is OperationCanceledException or ObjectDisposedException)
        {
            Release(acquired);
            throw;
        }
    }

    /// <inheritdoc />
    public IDisposable? TryAcquire(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return null;

        var gate = _locks.GetOrAdd(
            sessionId.Trim(),
            static _ => new SemaphoreSlim(1, 1));
        return gate.Wait(0) ? new SynchronousLease(gate) : null;
    }

    private static void Release(IReadOnlyList<SemaphoreSlim> acquired)
    {
        for (var index = acquired.Count - 1; index >= 0; index--)
            acquired[index].Release();
    }

    private sealed class SessionOperationLease(
        IReadOnlyList<SemaphoreSlim> acquired) : IAsyncDisposable
    {
        private IReadOnlyList<SemaphoreSlim>? _acquired = acquired;

        public ValueTask DisposeAsync()
        {
            var gates = Interlocked.Exchange(ref _acquired, null);
            if (gates is not null)
                Release(gates);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class EmptyLease : IAsyncDisposable
    {
        public static EmptyLease Instance { get; } = new();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class SynchronousLease(SemaphoreSlim gate) : IDisposable
    {
        private SemaphoreSlim? _gate = gate;

        public void Dispose()
        {
            Interlocked.Exchange(ref _gate, null)?.Release();
        }
    }
}
