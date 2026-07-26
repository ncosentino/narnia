using Microsoft.Data.Sqlite;
using NexusLabs.Narnia.Core.Configuration;
using NexusLabs.Narnia.Core.Models;
using NexusLabs.Narnia.Core.Repositories;

namespace NexusLabs.Narnia.Core.Tests;

public sealed class SqliteScheduledJobImportRepositoryTests : IDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly SqliteConnection _keepAlive;
    private readonly SqliteScheduledJobRegistry _jobs;
    private readonly SqliteScheduledJobImportRepository _imports;

    public SqliteScheduledJobImportRepositoryTests()
    {
        var databaseName = $"narnia_imports_{Guid.NewGuid():N}";
        var connectionString = $"Data Source={databaseName};Mode=Memory;Cache=Shared";
        _keepAlive = new SqliteConnection(connectionString);
        _keepAlive.Open();
        ApplyMigration("0007_add_scheduled_jobs.sql");
        ApplyMigration("0008_scheduled_jobs_definition.sql");
        ApplyMigration("0014_add_scheduled_job_imports.sql");
        var options = new NarniaOptions { SettingsConnectionString = connectionString };
        _jobs = new SqliteScheduledJobRegistry(options);
        _imports = new SqliteScheduledJobImportRepository(options);
    }

    public void Dispose() => _keepAlive.Dispose();

    [Fact]
    public async Task GetActiveAsync_ReturnsOnlyImportsWhoseLocalJobStillExists()
    {
        var now = DateTimeOffset.UtcNow;
        var job = await _jobs.CreateAsync(
            new ScheduledJobDraft(
                "Imported",
                null,
                null,
                "Daily 05:00",
                null,
                null,
                null,
                null,
                @"\Narnia\",
                "Imported",
                null,
                []),
            now,
            Ct);
        var record = new ScheduledJobImportRecord(
            job.Id,
            "package-1",
            "portable-1",
            "fingerprint",
            "source-1",
            now);
        await _imports.AddAsync(record, Ct);

        Assert.Single(await _imports.GetActiveAsync("package-1", "portable-1", Ct));
        Assert.Single(await _imports.GetByJobIdsAsync([job.Id], Ct));

        await _jobs.DeleteAsync(job.Id, Ct);

        Assert.Empty(await _imports.GetActiveAsync("package-1", "portable-1", Ct));
    }

    [Fact]
    public async Task DeleteAsync_RemovesRollbackProvenance()
    {
        var now = DateTimeOffset.UtcNow;
        var job = await _jobs.CreateAsync(
            new ScheduledJobDraft(
                "Imported",
                null,
                null,
                "Daily 05:00",
                null,
                null,
                null,
                null,
                @"\Narnia\",
                "Imported",
                null,
                []),
            now,
            Ct);
        await _imports.AddAsync(
            new ScheduledJobImportRecord(
                job.Id,
                "package-1",
                "portable-1",
                "fingerprint",
                null,
                now),
            Ct);

        await _imports.DeleteAsync(job.Id, Ct);

        Assert.Empty(await _imports.GetActiveAsync("package-1", "portable-1", Ct));
    }

    private void ApplyMigration(string fileName)
    {
        var assembly = typeof(SqliteScheduledJobRegistry).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith(fileName, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        using var command = _keepAlive.CreateCommand();
        command.CommandText = reader.ReadToEnd();
        command.ExecuteNonQuery();
    }
}
