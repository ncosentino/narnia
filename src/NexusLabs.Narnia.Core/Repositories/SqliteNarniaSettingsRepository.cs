using Microsoft.Data.Sqlite;
using NexusLabs.Narnia.Core.Configuration;

namespace NexusLabs.Narnia.Core.Repositories;

public sealed class SqliteNarniaSettingsRepository(NarniaOptions options) : INarniaSettingsRepository
{
    private readonly string _connectionString = options.SettingsConnectionString
        ?? $"Data Source={options.SettingsDatabasePath}";

    public async ValueTask<string?> GetAsync(string key, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM narnia_settings WHERE key = @key";
        cmd.Parameters.AddWithValue("@key", key);

        var result = await cmd.ExecuteScalarAsync(ct);
        return result as string;
    }

    public async ValueTask SetAsync(string key, string value, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            INSERT INTO narnia_settings (key, value) VALUES (@key, @value)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value
            """;
        cmd.Parameters.AddWithValue("@key", key);
        cmd.Parameters.AddWithValue("@value", value);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async ValueTask<Dictionary<string, string>> GetAllAsync(CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT key, value FROM narnia_settings";

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        while (await reader.ReadAsync(ct))
            result[reader.GetString(0)] = reader.GetString(1);
        return result;
    }

    public async ValueTask DeleteAsync(string key, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM narnia_settings WHERE key = @key";
        cmd.Parameters.AddWithValue("@key", key);

        await cmd.ExecuteNonQueryAsync(ct);
    }
}
