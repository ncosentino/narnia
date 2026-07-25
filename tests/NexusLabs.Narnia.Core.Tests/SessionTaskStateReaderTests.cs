using Microsoft.Data.Sqlite;
using System.IO.Abstractions;
using NexusLabs.Narnia.Core.Configuration;
using NexusLabs.Narnia.Core.Repositories;

namespace NexusLabs.Narnia.Core.Tests;

public sealed class SessionTaskStateReaderTests : IDisposable
{
    private const string SessionId = "22222222-2222-4222-8222-222222222222";
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"narnia_task_state_{Guid.NewGuid():N}");

    [Fact]
    public void Read_ExistingWorkspaceDatabase_ReturnsTodosAndDependencies()
    {
        var sessionDirectory = Path.Combine(_root, SessionId);
        Directory.CreateDirectory(sessionDirectory);
        var databasePath = Path.Combine(sessionDirectory, "session.db");
        using (var connection = new SqliteConnection($"Data Source={databasePath}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                CREATE TABLE todos (
                    id TEXT PRIMARY KEY,
                    title TEXT NOT NULL,
                    description TEXT,
                    status TEXT NOT NULL,
                    created_at TEXT,
                    updated_at TEXT);
                CREATE TABLE todo_deps (
                    todo_id TEXT NOT NULL,
                    depends_on TEXT NOT NULL);
                INSERT INTO todos VALUES (
                    'ship', 'Ship recovery', 'Finish the migration', 'in_progress',
                    '2026-07-24T12:00:00Z', '2026-07-24T13:00:00Z');
                INSERT INTO todos VALUES (
                    'design', 'Design recovery', NULL, 'done',
                    '2026-07-24T10:00:00Z', '2026-07-24T11:00:00Z');
                INSERT INTO todo_deps VALUES ('ship', 'design');
                """;
            command.ExecuteNonQuery();
        }
        var reader = new SessionTaskStateReader(
            new NarniaOptions { SessionStatePath = _root },
            new FileSystem());

        var result = reader.Read(SessionId);

        Assert.Null(result.Error);
        Assert.Equal(2, result.Todos.Count);
        Assert.Equal("in_progress", result.Todos.Single(todo => todo.Id == "ship").Status);
        var dependency = Assert.Single(result.Dependencies);
        Assert.Equal("ship", dependency.TaskId);
        Assert.Equal("design", dependency.DependsOn);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
