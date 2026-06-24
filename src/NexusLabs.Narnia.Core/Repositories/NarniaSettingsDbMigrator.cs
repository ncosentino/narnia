using System.Reflection;
using DbUp;
using DbUp.Sqlite;
using NexusLabs.Narnia.Core.Configuration;

namespace NexusLabs.Narnia.Core.Repositories;

public sealed class NarniaSettingsDbMigrator(NarniaOptions options)
{
    private readonly string _connectionString = options.SettingsConnectionString
        ?? $"Data Source={options.SettingsDatabasePath}";

    public void MigrateUp()
    {
        // SQLite creates the database file on first connection but not its parent directory.
        // Only relevant for the file-backed path (a connection-string override is for tests).
        if (options.SettingsConnectionString is null)
        {
            var directory = Path.GetDirectoryName(options.SettingsDatabasePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
        }

        var upgrader = DeployChanges.To
            .SqliteDatabase(_connectionString)
            .WithScriptsEmbeddedInAssembly(
                Assembly.GetExecutingAssembly(),
                s => s.Contains("Migrations"))
            .LogToConsole()
            .Build();

        var result = upgrader.PerformUpgrade();
        if (!result.Successful)
            throw new InvalidOperationException(
                "Narnia settings database migration failed.", result.Error);
    }
}
