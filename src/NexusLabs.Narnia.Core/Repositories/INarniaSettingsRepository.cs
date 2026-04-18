namespace NexusLabs.Narnia.Core.Repositories;

public interface INarniaSettingsRepository
{
    ValueTask<string?> GetAsync(string key, CancellationToken ct = default);
    ValueTask SetAsync(string key, string value, CancellationToken ct = default);
    ValueTask<Dictionary<string, string>> GetAllAsync(CancellationToken ct = default);
    ValueTask DeleteAsync(string key, CancellationToken ct = default);
}
