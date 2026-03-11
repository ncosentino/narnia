using Microsoft.Data.Sqlite;
using NexusLabs.Narnia.Core.Configuration;
using NexusLabs.Narnia.Core.Models;

namespace NexusLabs.Narnia.Core.Repositories;

public sealed class SqliteSessionOverridesRepository(NarniaOptions options) : ISessionOverridesRepository
{
    private readonly string _connectionString = options.SettingsConnectionString
        ?? $"Data Source={options.SettingsDatabasePath}";

    public async ValueTask<SessionOverride?> GetOverrideAsync(string sessionId, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            SELECT session_id, display_name, repository, branch, notes, created_at, updated_at
            FROM session_overrides
            WHERE session_id = @session_id
            """;
        cmd.Parameters.AddWithValue("@session_id", sessionId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;

        return ReadSessionOverride(reader);
    }

    public async ValueTask UpsertOverrideAsync(SessionOverride sessionOverride, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            INSERT INTO session_overrides (session_id, display_name, repository, branch, notes, created_at, updated_at)
            VALUES (@session_id, @display_name, @repository, @branch, @notes, @created_at, @updated_at)
            ON CONFLICT(session_id) DO UPDATE SET
                display_name = excluded.display_name,
                repository   = excluded.repository,
                branch       = excluded.branch,
                notes        = excluded.notes,
                updated_at   = excluded.updated_at
            """;
        cmd.Parameters.AddWithValue("@session_id", sessionOverride.SessionId);
        cmd.Parameters.AddWithValue("@display_name", (object?)sessionOverride.DisplayName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@repository", (object?)sessionOverride.Repository ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@branch", (object?)sessionOverride.Branch ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@notes", (object?)sessionOverride.Notes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@created_at", sessionOverride.CreatedAt.ToString("o"));
        cmd.Parameters.AddWithValue("@updated_at", sessionOverride.UpdatedAt.ToString("o"));

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async ValueTask DeleteOverrideAsync(string sessionId, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM session_overrides WHERE session_id = @session_id";
        cmd.Parameters.AddWithValue("@session_id", sessionId);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static SessionOverride ReadSessionOverride(SqliteDataReader reader)
    {
        var sessionId = reader.GetString(0);
        var displayName = reader.IsDBNull(1) ? null : reader.GetString(1);
        var repository = reader.IsDBNull(2) ? null : reader.GetString(2);
        var branch = reader.IsDBNull(3) ? null : reader.GetString(3);
        var notes = reader.IsDBNull(4) ? null : reader.GetString(4);
        var createdAt = ParseDateTimeOffset(reader.IsDBNull(5) ? null : reader.GetString(5));
        var updatedAt = ParseDateTimeOffset(reader.IsDBNull(6) ? null : reader.GetString(6));

        return new SessionOverride(sessionId, displayName, repository, branch, notes, createdAt, updatedAt);
    }

    private static DateTimeOffset ParseDateTimeOffset(string? value) =>
        DateTimeOffset.TryParse(value, out var dt) ? dt : DateTimeOffset.MinValue;
}
