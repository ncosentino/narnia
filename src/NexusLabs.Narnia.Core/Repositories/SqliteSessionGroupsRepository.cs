using Microsoft.Data.Sqlite;
using NexusLabs.Narnia.Core.Configuration;
using NexusLabs.Narnia.Core.Models;

namespace NexusLabs.Narnia.Core.Repositories;

public sealed class SqliteSessionGroupsRepository(NarniaOptions options) : ISessionGroupsRepository
{
    private const string GroupColumns = "id, name, created_at, updated_at";

    private readonly string _connectionString = options.SettingsConnectionString
        ?? $"Data Source={options.SettingsDatabasePath}";

    public async ValueTask<IReadOnlyList<SessionGroup>> GetAllAsync(CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {GroupColumns} FROM session_groups ORDER BY updated_at DESC";
        return await ReadGroupsAsync(conn, cmd, ct);
    }

    public async ValueTask<SessionGroup?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {GroupColumns} FROM session_groups WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);

        var groups = await ReadGroupsAsync(conn, cmd, ct);
        return groups.Count > 0 ? groups[0] : null;
    }

    public async ValueTask<SessionGroup> CreateAsync(
        string name,
        IReadOnlyList<string> sessionIds,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        var id = Guid.NewGuid().ToString();
        var members = ToMembers(sessionIds);

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(ct);

        await using (var insert = conn.CreateCommand())
        {
            insert.Transaction = tx;
            insert.CommandText =
                $"INSERT INTO session_groups ({GroupColumns}) VALUES (@id, @name, @now, @now)";
            insert.Parameters.AddWithValue("@id", id);
            insert.Parameters.AddWithValue("@name", name);
            insert.Parameters.AddWithValue("@now", now.ToString("o"));
            await insert.ExecuteNonQueryAsync(ct);
        }

        await InsertMembersAsync(conn, tx, id, members, ct);
        await tx.CommitAsync(ct);

        return new SessionGroup(id, name, now, now, members);
    }

    public async ValueTask RenameAsync(string id, string name, DateTimeOffset now, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE session_groups SET name = @name, updated_at = @now WHERE id = @id";
        cmd.Parameters.AddWithValue("@name", name);
        cmd.Parameters.AddWithValue("@now", now.ToString("o"));
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async ValueTask SetMembersAsync(
        string id,
        IReadOnlyList<string> sessionIds,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        var members = ToMembers(sessionIds);

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(ct);

        await DeleteMembersAsync(conn, tx, id, ct);
        await InsertMembersAsync(conn, tx, id, members, ct);

        await using (var touch = conn.CreateCommand())
        {
            touch.Transaction = tx;
            touch.CommandText = "UPDATE session_groups SET updated_at = @now WHERE id = @id";
            touch.Parameters.AddWithValue("@now", now.ToString("o"));
            touch.Parameters.AddWithValue("@id", id);
            await touch.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
    }

    public async ValueTask DeleteAsync(string id, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(ct);

        await DeleteMembersAsync(conn, tx, id, ct);
        await using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM session_groups WHERE id = @id";
            cmd.Parameters.AddWithValue("@id", id);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
    }

    // Collapses duplicate session ids to their first occurrence and assigns sequential order.
    private static List<SessionGroupMember> ToMembers(IReadOnlyList<string> sessionIds)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var members = new List<SessionGroupMember>(sessionIds.Count);
        foreach (var sessionId in sessionIds)
        {
            if (string.IsNullOrWhiteSpace(sessionId) || !seen.Add(sessionId))
                continue;
            members.Add(new SessionGroupMember(sessionId, members.Count));
        }

        return members;
    }

    private static async ValueTask InsertMembersAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        string groupId,
        IReadOnlyList<SessionGroupMember> members,
        CancellationToken ct)
    {
        foreach (var member in members)
        {
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText =
                "INSERT INTO session_group_members (group_id, session_id, member_order) VALUES (@g, @s, @o)";
            cmd.Parameters.AddWithValue("@g", groupId);
            cmd.Parameters.AddWithValue("@s", member.SessionId);
            cmd.Parameters.AddWithValue("@o", member.MemberOrder);
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    private static async ValueTask DeleteMembersAsync(
        SqliteConnection conn, SqliteTransaction tx, string groupId, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "DELETE FROM session_group_members WHERE group_id = @id";
        cmd.Parameters.AddWithValue("@id", groupId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async ValueTask<IReadOnlyList<SessionGroup>> ReadGroupsAsync(
        SqliteConnection conn, SqliteCommand groupsCommand, CancellationToken ct)
    {
        var rows = new List<(string Id, string Name, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt)>();
        await using (var reader = await groupsCommand.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                rows.Add((
                    reader.GetString(0),
                    reader.GetString(1),
                    ParseTimestamp(reader.GetString(2)),
                    ParseTimestamp(reader.GetString(3))));
            }
        }

        if (rows.Count == 0)
            return [];

        var membersByGroup = await LoadMembersAsync(conn, rows.Select(r => r.Id).ToList(), ct);

        var result = new List<SessionGroup>(rows.Count);
        foreach (var row in rows)
        {
            var members = membersByGroup.TryGetValue(row.Id, out var m) ? m : [];
            result.Add(new SessionGroup(row.Id, row.Name, row.CreatedAt, row.UpdatedAt, members));
        }

        return result;
    }

    private static async ValueTask<Dictionary<string, List<SessionGroupMember>>> LoadMembersAsync(
        SqliteConnection conn, IReadOnlyList<string> groupIds, CancellationToken ct)
    {
        var result = new Dictionary<string, List<SessionGroupMember>>(StringComparer.Ordinal);
        if (groupIds.Count == 0)
            return result;

        await using var cmd = conn.CreateCommand();
        var parameters = new List<string>(groupIds.Count);
        for (var i = 0; i < groupIds.Count; i++)
        {
            var name = $"@g{i}";
            parameters.Add(name);
            cmd.Parameters.AddWithValue(name, groupIds[i]);
        }

        cmd.CommandText =
            $"SELECT group_id, session_id, member_order FROM session_group_members WHERE group_id IN ({string.Join(", ", parameters)}) ORDER BY member_order";

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var groupId = reader.GetString(0);
            var member = new SessionGroupMember(reader.GetString(1), reader.GetInt32(2));
            if (!result.TryGetValue(groupId, out var list))
            {
                list = [];
                result[groupId] = list;
            }

            list.Add(member);
        }

        return result;
    }

    private static DateTimeOffset ParseTimestamp(string value) =>
        DateTimeOffset.TryParse(value, out var dt) ? dt : DateTimeOffset.MinValue;
}
