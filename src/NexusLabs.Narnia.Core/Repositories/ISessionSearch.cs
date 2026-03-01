using NexusLabs.Narnia.Core.Models;

namespace NexusLabs.Narnia.Core.Repositories;

public interface ISessionSearch
{
    ValueTask<SearchResult[]> SearchAsync(string query, int limit = 20, CancellationToken ct = default);
}
