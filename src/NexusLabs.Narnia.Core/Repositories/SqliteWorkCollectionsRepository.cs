using Microsoft.Data.Sqlite;
using NexusLabs.Narnia.Core.Configuration;
using NexusLabs.Narnia.Core.Models;
using System.Text;

namespace NexusLabs.Narnia.Core.Repositories;

/// <summary>
/// Stores work collections and explicit session memberships in Narnia's SQLite settings database.
/// </summary>
public sealed class SqliteWorkCollectionsRepository(NarniaOptions options) : IWorkCollectionsRepository
{
    private const string CollectionColumns = "id, name, created_at, updated_at";

    private readonly string _connectionString = options.SettingsConnectionString
        ?? $"Data Source={options.SettingsDatabasePath}";

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<WorkCollection>> GetAllAsync(CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText =
            $"SELECT {CollectionColumns} FROM work_collections ORDER BY name_key, id";
        return await ReadCollectionsAsync(connection, command, ct);
    }

    /// <inheritdoc />
    public async ValueTask<WorkCollection?> GetByIdAsync(
        string id,
        CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {CollectionColumns} FROM work_collections WHERE id = @id";
        command.Parameters.AddWithValue("@id", id);

        var collections = await ReadCollectionsAsync(connection, command, ct);
        return collections.Count == 0 ? null : collections[0];
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<WorkCollection>> GetBySessionIdAsync(
        string sessionId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return [];

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            SELECT c.{CollectionColumns.Replace(", ", ", c.")}
            FROM work_collections c
            INNER JOIN work_collection_sessions m ON m.collection_id = c.id
            WHERE m.session_id = @sessionId
            ORDER BY c.name_key, c.id
            """;
        command.Parameters.AddWithValue("@sessionId", sessionId);
        return await ReadCollectionsAsync(connection, command, ct);
    }

    /// <inheritdoc />
    public async ValueTask<WorkCollection> CreateAsync(
        string name,
        IReadOnlyCollection<string> sessionIds,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        var (normalizedName, nameKey) = NormalizeName(name);
        var members = SessionIdCollection.Normalize(sessionIds)
            .Select(sessionId => new WorkCollectionMember(sessionId, now))
            .ToArray();
        var id = Guid.NewGuid().ToString();

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var transaction =
            (SqliteTransaction)await connection.BeginTransactionAsync(ct);

        try
        {
            await using (var insert = connection.CreateCommand())
            {
                insert.Transaction = transaction;
                insert.CommandText =
                    """
                    INSERT INTO work_collections (id, name, name_key, created_at, updated_at)
                    VALUES (@id, @name, @nameKey, @now, @now)
                    """;
                insert.Parameters.AddWithValue("@id", id);
                insert.Parameters.AddWithValue("@name", normalizedName);
                insert.Parameters.AddWithValue("@nameKey", nameKey);
                insert.Parameters.AddWithValue("@now", now.ToString("o"));
                await insert.ExecuteNonQueryAsync(ct);
            }

            await InsertMembersAsync(connection, transaction, id, members, ct);
            await transaction.CommitAsync(ct);
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
        {
            throw new WorkCollectionNameConflictException(normalizedName, exception);
        }

        return new WorkCollection(id, normalizedName, now, now, members);
    }

    /// <inheritdoc />
    public async ValueTask<bool> RenameAsync(
        string id,
        string name,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        var (normalizedName, nameKey) = NormalizeName(name);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                UPDATE work_collections
                SET name = @name, name_key = @nameKey, updated_at = @now
                WHERE id = @id
                """;
            command.Parameters.AddWithValue("@name", normalizedName);
            command.Parameters.AddWithValue("@nameKey", nameKey);
            command.Parameters.AddWithValue("@now", now.ToString("o"));
            command.Parameters.AddWithValue("@id", id);
            return await command.ExecuteNonQueryAsync(ct) > 0;
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
        {
            throw new WorkCollectionNameConflictException(normalizedName, exception);
        }
    }

    /// <inheritdoc />
    public ValueTask<int?> AddSessionsAsync(
        string id,
        IReadOnlyCollection<string> sessionIds,
        DateTimeOffset now,
        CancellationToken ct = default) =>
        ChangeMembershipAsync(id, sessionIds, now, add: true, ct);

    /// <inheritdoc />
    public ValueTask<int?> RemoveSessionsAsync(
        string id,
        IReadOnlyCollection<string> sessionIds,
        DateTimeOffset now,
        CancellationToken ct = default) =>
        ChangeMembershipAsync(id, sessionIds, now, add: false, ct);

    /// <inheritdoc />
    public async ValueTask<bool> DeleteAsync(string id, CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var transaction =
            (SqliteTransaction)await connection.BeginTransactionAsync(ct);

        await DeleteMembersAsync(connection, transaction, id, ct);

        await using var delete = connection.CreateCommand();
        delete.Transaction = transaction;
        delete.CommandText = "DELETE FROM work_collections WHERE id = @id";
        delete.Parameters.AddWithValue("@id", id);
        var deleted = await delete.ExecuteNonQueryAsync(ct) > 0;

        await transaction.CommitAsync(ct);
        return deleted;
    }

    private async ValueTask<int?> ChangeMembershipAsync(
        string id,
        IReadOnlyCollection<string> sessionIds,
        DateTimeOffset now,
        bool add,
        CancellationToken ct)
    {
        var normalizedIds = SessionIdCollection.Normalize(sessionIds);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var transaction =
            (SqliteTransaction)await connection.BeginTransactionAsync(ct);

        if (!await CollectionExistsAsync(connection, transaction, id, ct))
        {
            await transaction.RollbackAsync(ct);
            return null;
        }

        var changed = 0;
        foreach (var sessionId in normalizedIds)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = add
                ? """
                  INSERT OR IGNORE INTO work_collection_sessions (collection_id, session_id, added_at)
                  VALUES (@collectionId, @sessionId, @now)
                  """
                : """
                  DELETE FROM work_collection_sessions
                  WHERE collection_id = @collectionId AND session_id = @sessionId
                  """;
            command.Parameters.AddWithValue("@collectionId", id);
            command.Parameters.AddWithValue("@sessionId", sessionId);
            if (add)
                command.Parameters.AddWithValue("@now", now.ToString("o"));
            changed += await command.ExecuteNonQueryAsync(ct);
        }

        if (changed > 0)
        {
            await using var touch = connection.CreateCommand();
            touch.Transaction = transaction;
            touch.CommandText =
                "UPDATE work_collections SET updated_at = @now WHERE id = @id";
            touch.Parameters.AddWithValue("@now", now.ToString("o"));
            touch.Parameters.AddWithValue("@id", id);
            await touch.ExecuteNonQueryAsync(ct);
        }

        await transaction.CommitAsync(ct);
        return changed;
    }

    private static async ValueTask<bool> CollectionExistsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string id,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COUNT(*) FROM work_collections WHERE id = @id";
        command.Parameters.AddWithValue("@id", id);
        return (long)(await command.ExecuteScalarAsync(ct))! > 0;
    }

    private static async ValueTask InsertMembersAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string collectionId,
        IReadOnlyList<WorkCollectionMember> members,
        CancellationToken ct)
    {
        foreach (var member in members)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                """
                INSERT INTO work_collection_sessions (collection_id, session_id, added_at)
                VALUES (@collectionId, @sessionId, @addedAt)
                """;
            command.Parameters.AddWithValue("@collectionId", collectionId);
            command.Parameters.AddWithValue("@sessionId", member.SessionId);
            command.Parameters.AddWithValue("@addedAt", member.AddedAt.ToString("o"));
            await command.ExecuteNonQueryAsync(ct);
        }
    }

    private static async ValueTask DeleteMembersAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string collectionId,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "DELETE FROM work_collection_sessions WHERE collection_id = @collectionId";
        command.Parameters.AddWithValue("@collectionId", collectionId);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async ValueTask<IReadOnlyList<WorkCollection>> ReadCollectionsAsync(
        SqliteConnection connection,
        SqliteCommand collectionsCommand,
        CancellationToken ct)
    {
        var rows =
            new List<(string Id, string Name, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt)>();
        await using (var reader = await collectionsCommand.ExecuteReaderAsync(ct))
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

        var membersByCollection = await LoadMembersAsync(
            connection,
            rows.Select(row => row.Id).ToArray(),
            ct);
        return
        [
            .. rows.Select(row => new WorkCollection(
                row.Id,
                row.Name,
                row.CreatedAt,
                row.UpdatedAt,
                membersByCollection.TryGetValue(row.Id, out var members) ? members : [])),
        ];
    }

    private static async ValueTask<Dictionary<string, List<WorkCollectionMember>>> LoadMembersAsync(
        SqliteConnection connection,
        IReadOnlyList<string> collectionIds,
        CancellationToken ct)
    {
        var result =
            new Dictionary<string, List<WorkCollectionMember>>(StringComparer.Ordinal);
        if (collectionIds.Count == 0)
            return result;

        await using var command = connection.CreateCommand();
        var parameterNames = new string[collectionIds.Count];
        for (var i = 0; i < collectionIds.Count; i++)
        {
            parameterNames[i] = $"@collectionId{i}";
            command.Parameters.AddWithValue(parameterNames[i], collectionIds[i]);
        }

        command.CommandText =
            $"""
            SELECT collection_id, session_id, added_at
            FROM work_collection_sessions
            WHERE collection_id IN ({string.Join(", ", parameterNames)})
            ORDER BY added_at DESC, session_id
            """;

        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var collectionId = reader.GetString(0);
            if (!result.TryGetValue(collectionId, out var members))
            {
                members = [];
                result[collectionId] = members;
            }

            members.Add(new WorkCollectionMember(
                reader.GetString(1),
                ParseTimestamp(reader.GetString(2))));
        }

        return result;
    }

    private static (string Name, string Key) NormalizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A collection name is required.", nameof(name));

        var normalizedName = name.Trim();
        var key = normalizedName
            .Normalize(NormalizationForm.FormKC)
            .ToUpperInvariant();
        return (normalizedName, key);
    }

    private static DateTimeOffset ParseTimestamp(string value) =>
        DateTimeOffset.TryParse(value, out var timestamp)
            ? timestamp
            : DateTimeOffset.MinValue;
}
