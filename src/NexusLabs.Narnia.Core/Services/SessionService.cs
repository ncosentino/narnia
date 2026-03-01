using NexusLabs.Narnia.Core.Models;
using NexusLabs.Narnia.Core.Repositories;

namespace NexusLabs.Narnia.Core.Services;

public sealed class SessionService(ISessionRepository repository, ISessionSearch search)
{
    public ISessionRepository Repository { get; } = repository;
    public ISessionSearch Search { get; } = search;

    public async ValueTask<(Session? session, Checkpoint[] checkpoints)> GetSessionWithCheckpointsAsync(
        string sessionId, CancellationToken ct = default)
    {
        var session = await Repository.GetByIdAsync(sessionId, ct);
        if (session is null)
            return (null, []);
        var checkpoints = await Repository.GetCheckpointsAsync(sessionId, ct);
        return (session, checkpoints);
    }
}
