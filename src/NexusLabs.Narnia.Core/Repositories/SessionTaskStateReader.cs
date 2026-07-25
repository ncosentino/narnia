using Microsoft.Data.Sqlite;
using System.IO.Abstractions;
using NexusLabs.Narnia.Core.Configuration;
using NexusLabs.Narnia.Core.Models;

namespace NexusLabs.Narnia.Core.Repositories;

/// <summary>Reads Copilot workspace task tables through a read-only SQLite connection.</summary>
public sealed class SessionTaskStateReader(
    NarniaOptions options,
    IFileSystem fileSystem) : ISessionTaskStateReader
{
    /// <inheritdoc />
    public SessionTaskState Read(string sessionId)
    {
        if (!TryResolveDatabasePath(sessionId, out var databasePath) ||
            !fileSystem.File.Exists(databasePath))
        {
            return new SessionTaskState([], [], null);
        }

        try
        {
            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadOnly,
            };
            using var connection = new SqliteConnection(builder.ToString());
            connection.Open();
            if (!HasTable(connection, "todos") || !HasTable(connection, "todo_deps"))
                return new SessionTaskState([], [], null);

            var todos = ReadTodos(connection);
            var dependencies = ReadDependencies(connection);
            return new SessionTaskState(todos, dependencies, null);
        }
        catch (Exception exception) when (
            exception is SqliteException or IOException or UnauthorizedAccessException)
        {
            return new SessionTaskState(
                [],
                [],
                $"Workspace tasks could not be read: {exception.Message}");
        }
    }

    private static bool HasTable(SqliteConnection connection, string table)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = @table";
        command.Parameters.AddWithValue("@table", table);
        return (long)(command.ExecuteScalar() ?? 0L) > 0;
    }

    private static IReadOnlyList<SessionTaskItem> ReadTodos(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, title, description, status, created_at, updated_at
            FROM todos
            ORDER BY created_at, id
            """;
        using var reader = command.ExecuteReader();
        var todos = new List<SessionTaskItem>();
        while (reader.Read())
        {
            todos.Add(new SessionTaskItem(
                reader.GetString(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetString(3),
                ParseTimestamp(reader, 4),
                ParseTimestamp(reader, 5)));
        }

        return todos;
    }

    private static IReadOnlyList<SessionTaskDependency> ReadDependencies(
        SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT todo_id, depends_on
            FROM todo_deps
            ORDER BY todo_id, depends_on
            """;
        using var reader = command.ExecuteReader();
        var dependencies = new List<SessionTaskDependency>();
        while (reader.Read())
            dependencies.Add(new SessionTaskDependency(reader.GetString(0), reader.GetString(1)));
        return dependencies;
    }

    private static DateTimeOffset? ParseTimestamp(SqliteDataReader reader, int ordinal) =>
        !reader.IsDBNull(ordinal) &&
        DateTimeOffset.TryParse(reader.GetString(ordinal), out var timestamp)
            ? timestamp
            : null;

    private bool TryResolveDatabasePath(string sessionId, out string databasePath)
    {
        var root = fileSystem.Path.GetFullPath(options.SessionStatePath)
            .TrimEnd(
                fileSystem.Path.DirectorySeparatorChar,
                fileSystem.Path.AltDirectorySeparatorChar);
        var sessionDirectory = fileSystem.Path.GetFullPath(
            fileSystem.Path.Combine(root, sessionId));
        databasePath = fileSystem.Path.Combine(sessionDirectory, "session.db");
        return Guid.TryParse(sessionId, out _)
            && string.Equals(
                fileSystem.Path.GetDirectoryName(sessionDirectory),
                root,
                OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal);
    }
}
