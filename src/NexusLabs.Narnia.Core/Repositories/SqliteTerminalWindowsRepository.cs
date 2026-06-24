using Microsoft.Data.Sqlite;
using NexusLabs.Narnia.Core.Configuration;
using NexusLabs.Narnia.Core.Models;

namespace NexusLabs.Narnia.Core.Repositories;

public sealed class SqliteTerminalWindowsRepository(NarniaOptions options) : ITerminalWindowsRepository
{
    private const string OpenStatus = "open";
    private const string ClosedStatus = "closed";

    private const string WindowColumns =
        "id, name, pinned, source, status, terminal_pid, composition_key, occurrence_count, first_seen_at, last_seen_at, closed_at";

    private readonly string _connectionString = options.SettingsConnectionString
        ?? $"Data Source={options.SettingsDatabasePath}";

    public async ValueTask<IReadOnlyList<TerminalWindow>> GetOpenAsync(CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            $"SELECT {WindowColumns} FROM terminal_windows WHERE status = '{OpenStatus}' ORDER BY last_seen_at DESC";
        return await ReadWindowsAsync(conn, cmd, ct);
    }

    public async ValueTask<IReadOnlyList<TerminalWindow>> GetClosedAsync(int limit, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            $"SELECT {WindowColumns} FROM terminal_windows WHERE status = '{ClosedStatus}' ORDER BY last_seen_at DESC LIMIT @limit";
        cmd.Parameters.AddWithValue("@limit", limit);
        return await ReadWindowsAsync(conn, cmd, ct);
    }

    public async ValueTask<TerminalWindow?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {WindowColumns} FROM terminal_windows WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);

        var windows = await ReadWindowsAsync(conn, cmd, ct);
        return windows.Count > 0 ? windows[0] : null;
    }

    public async ValueTask UpsertOpenAsync(
        int terminalProcessId,
        string compositionKey,
        IReadOnlyList<TerminalWindowTab> tabs,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(ct);

        string? existingId;
        await using (var find = conn.CreateCommand())
        {
            find.Transaction = tx;
            find.CommandText =
                $"SELECT id FROM terminal_windows WHERE status = '{OpenStatus}' AND composition_key = @key LIMIT 1";
            find.Parameters.AddWithValue("@key", compositionKey);
            existingId = await find.ExecuteScalarAsync(ct) as string;
        }

        if (existingId is not null)
        {
            await using var update = conn.CreateCommand();
            update.Transaction = tx;
            update.CommandText =
                "UPDATE terminal_windows SET composition_key = @key, terminal_pid = @pid, last_seen_at = @now WHERE id = @id";
            update.Parameters.AddWithValue("@key", compositionKey);
            update.Parameters.AddWithValue("@pid", terminalProcessId);
            update.Parameters.AddWithValue("@now", now.ToString("o"));
            update.Parameters.AddWithValue("@id", existingId);
            await update.ExecuteNonQueryAsync(ct);

            await DeleteTabsAsync(conn, tx, existingId, ct);
            await InsertTabsAsync(conn, tx, existingId, tabs, ct);
        }
        else
        {
            var id = Guid.NewGuid().ToString();
            await using (var insert = conn.CreateCommand())
            {
                insert.Transaction = tx;
                insert.CommandText =
                    $"""
                    INSERT INTO terminal_windows ({WindowColumns})
                    VALUES (@id, NULL, 0, 'live', '{OpenStatus}', @pid, @key, 1, @now, @now, NULL)
                    """;
                insert.Parameters.AddWithValue("@id", id);
                insert.Parameters.AddWithValue("@pid", terminalProcessId);
                insert.Parameters.AddWithValue("@key", compositionKey);
                insert.Parameters.AddWithValue("@now", now.ToString("o"));
                await insert.ExecuteNonQueryAsync(ct);
            }

            await InsertTabsAsync(conn, tx, id, tabs, ct);
        }

        await tx.CommitAsync(ct);
    }

    public async ValueTask CloseAsync(string id, DateTimeOffset now, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(ct);

        string compositionKey;
        await using (var load = conn.CreateCommand())
        {
            load.Transaction = tx;
            load.CommandText =
                $"SELECT composition_key FROM terminal_windows WHERE id = @id AND status = '{OpenStatus}'";
            load.Parameters.AddWithValue("@id", id);
            if (await load.ExecuteScalarAsync(ct) is not string key)
            {
                await tx.RollbackAsync(ct);
                return;
            }

            compositionKey = key;
        }

        string? mergeTargetId;
        long mergeTargetOccurrence = 0;
        await using (var find = conn.CreateCommand())
        {
            find.Transaction = tx;
            find.CommandText =
                $"""
                SELECT id, occurrence_count FROM terminal_windows
                WHERE status = '{ClosedStatus}' AND composition_key = @key AND id <> @id
                ORDER BY last_seen_at DESC LIMIT 1
                """;
            find.Parameters.AddWithValue("@key", compositionKey);
            find.Parameters.AddWithValue("@id", id);
            await using var reader = await find.ExecuteReaderAsync(ct);
            mergeTargetId = await reader.ReadAsync(ct) ? reader.GetString(0) : null;
            if (mergeTargetId is not null)
                mergeTargetOccurrence = reader.GetInt64(1);
        }

        if (mergeTargetId is not null)
        {
            await using (var bump = conn.CreateCommand())
            {
                bump.Transaction = tx;
                bump.CommandText =
                    "UPDATE terminal_windows SET occurrence_count = @occ, last_seen_at = @now, closed_at = @now WHERE id = @target";
                bump.Parameters.AddWithValue("@occ", mergeTargetOccurrence + 1);
                bump.Parameters.AddWithValue("@now", now.ToString("o"));
                bump.Parameters.AddWithValue("@target", mergeTargetId);
                await bump.ExecuteNonQueryAsync(ct);
            }

            await DeleteTabsAsync(conn, tx, mergeTargetId, ct);
            await using (var moveTabs = conn.CreateCommand())
            {
                moveTabs.Transaction = tx;
                moveTabs.CommandText = "UPDATE terminal_window_tabs SET window_id = @target WHERE window_id = @id";
                moveTabs.Parameters.AddWithValue("@target", mergeTargetId);
                moveTabs.Parameters.AddWithValue("@id", id);
                await moveTabs.ExecuteNonQueryAsync(ct);
            }

            await using var deleteSource = conn.CreateCommand();
            deleteSource.Transaction = tx;
            deleteSource.CommandText = "DELETE FROM terminal_windows WHERE id = @id";
            deleteSource.Parameters.AddWithValue("@id", id);
            await deleteSource.ExecuteNonQueryAsync(ct);
        }
        else
        {
            await using var close = conn.CreateCommand();
            close.Transaction = tx;
            close.CommandText =
                $"UPDATE terminal_windows SET status = '{ClosedStatus}', closed_at = @now, last_seen_at = @now, terminal_pid = NULL WHERE id = @id";
            close.Parameters.AddWithValue("@now", now.ToString("o"));
            close.Parameters.AddWithValue("@id", id);
            await close.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
    }

    public async ValueTask SetNameAsync(string id, string? name, bool pinned, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE terminal_windows SET name = @name, pinned = @pinned WHERE id = @id";
        cmd.Parameters.AddWithValue("@name", (object?)name ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@pinned", pinned ? 1 : 0);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async ValueTask DeleteAsync(string id, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(ct);

        await DeleteTabsAsync(conn, tx, id, ct);
        await using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM terminal_windows WHERE id = @id";
            cmd.Parameters.AddWithValue("@id", id);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
    }

    public async ValueTask PruneClosedAsync(int keepCount, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(ct);

        var staleIds = new List<string>();
        await using (var find = conn.CreateCommand())
        {
            find.Transaction = tx;
            find.CommandText =
                $"""
                SELECT id FROM terminal_windows
                WHERE status = '{ClosedStatus}' AND pinned = 0
                ORDER BY last_seen_at DESC LIMIT -1 OFFSET @keep
                """;
            find.Parameters.AddWithValue("@keep", Math.Max(0, keepCount));
            await using var reader = await find.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                staleIds.Add(reader.GetString(0));
        }

        foreach (var id in staleIds)
        {
            await DeleteTabsAsync(conn, tx, id, ct);
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM terminal_windows WHERE id = @id";
            cmd.Parameters.AddWithValue("@id", id);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
    }

    private static async ValueTask DeleteTabsAsync(
        SqliteConnection conn, SqliteTransaction tx, string windowId, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "DELETE FROM terminal_window_tabs WHERE window_id = @id";
        cmd.Parameters.AddWithValue("@id", windowId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async ValueTask InsertTabsAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        string windowId,
        IReadOnlyList<TerminalWindowTab> tabs,
        CancellationToken ct)
    {
        foreach (var tab in tabs)
        {
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText =
                "INSERT INTO terminal_window_tabs (window_id, session_id, tab_order, directory) VALUES (@w, @s, @o, @d)";
            cmd.Parameters.AddWithValue("@w", windowId);
            cmd.Parameters.AddWithValue("@s", tab.SessionId);
            cmd.Parameters.AddWithValue("@o", tab.TabOrder);
            cmd.Parameters.AddWithValue("@d", (object?)tab.Directory ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    private static async ValueTask<IReadOnlyList<TerminalWindow>> ReadWindowsAsync(
        SqliteConnection conn, SqliteCommand windowsCommand, CancellationToken ct)
    {
        var rows = new List<TerminalWindowRow>();
        await using (var reader = await windowsCommand.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
                rows.Add(ReadWindowRow(reader));
        }

        if (rows.Count == 0)
            return [];

        var tabsByWindow = await LoadTabsAsync(conn, rows.Select(r => r.Id).ToList(), ct);

        var result = new List<TerminalWindow>(rows.Count);
        foreach (var row in rows)
        {
            var tabs = tabsByWindow.TryGetValue(row.Id, out var t) ? t : [];
            result.Add(row.ToWindow(tabs));
        }

        return result;
    }

    private static async ValueTask<Dictionary<string, List<TerminalWindowTab>>> LoadTabsAsync(
        SqliteConnection conn, IReadOnlyList<string> windowIds, CancellationToken ct)
    {
        var result = new Dictionary<string, List<TerminalWindowTab>>(StringComparer.Ordinal);
        if (windowIds.Count == 0)
            return result;

        await using var cmd = conn.CreateCommand();
        var parameters = new List<string>(windowIds.Count);
        for (var i = 0; i < windowIds.Count; i++)
        {
            var name = $"@w{i}";
            parameters.Add(name);
            cmd.Parameters.AddWithValue(name, windowIds[i]);
        }

        cmd.CommandText =
            $"SELECT window_id, session_id, tab_order, directory FROM terminal_window_tabs WHERE window_id IN ({string.Join(", ", parameters)}) ORDER BY tab_order";

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var windowId = reader.GetString(0);
            var tab = new TerminalWindowTab(
                reader.GetString(1),
                reader.GetInt32(2),
                reader.IsDBNull(3) ? null : reader.GetString(3));

            if (!result.TryGetValue(windowId, out var list))
            {
                list = [];
                result[windowId] = list;
            }

            list.Add(tab);
        }

        return result;
    }

    private static TerminalWindowRow ReadWindowRow(SqliteDataReader reader) => new(
        reader.GetString(0),
        reader.IsDBNull(1) ? null : reader.GetString(1),
        reader.GetInt64(2) != 0,
        reader.GetString(3),
        string.Equals(reader.GetString(4), OpenStatus, StringComparison.Ordinal)
            ? TerminalWindowStatus.Open
            : TerminalWindowStatus.Closed,
        reader.IsDBNull(5) ? null : reader.GetInt32(5),
        reader.GetString(6),
        reader.GetInt32(7),
        ParseTimestamp(reader.GetString(8)),
        ParseTimestamp(reader.GetString(9)),
        reader.IsDBNull(10) ? null : ParseTimestamp(reader.GetString(10)));

    private static DateTimeOffset ParseTimestamp(string value) =>
        DateTimeOffset.TryParse(value, out var dt) ? dt : DateTimeOffset.MinValue;

    private sealed record TerminalWindowRow(
        string Id,
        string? Name,
        bool Pinned,
        string Source,
        TerminalWindowStatus Status,
        int? TerminalProcessId,
        string CompositionKey,
        int OccurrenceCount,
        DateTimeOffset FirstSeenAt,
        DateTimeOffset LastSeenAt,
        DateTimeOffset? ClosedAt)
    {
        public TerminalWindow ToWindow(IReadOnlyList<TerminalWindowTab> tabs) => new(
            Id, Name, Pinned, Source, Status, TerminalProcessId, CompositionKey,
            OccurrenceCount, FirstSeenAt, LastSeenAt, ClosedAt, tabs);
    }
}
