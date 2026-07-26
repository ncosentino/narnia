using Microsoft.Data.Sqlite;
using NexusLabs.Narnia.Core.Configuration;
using NexusLabs.Narnia.Core.Models;

namespace NexusLabs.Narnia.Core.Repositories;

/// <summary>SQLite implementation of <see cref="IScheduledJobImportRepository"/>.</summary>
public sealed class SqliteScheduledJobImportRepository(NarniaOptions options)
    : IScheduledJobImportRepository
{
    private readonly string _connectionString = options.SettingsConnectionString
        ?? $"Data Source={options.SettingsDatabasePath}";

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<ScheduledJobImportRecord>> GetByJobIdsAsync(
        IReadOnlyCollection<string> jobIds,
        CancellationToken ct)
    {
        if (jobIds.Count == 0)
            return [];

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        var parameters = new List<string>(jobIds.Count);
        var index = 0;
        foreach (var jobId in jobIds.Distinct(StringComparer.Ordinal))
        {
            var parameter = $"@job_{index++}";
            parameters.Add(parameter);
            command.Parameters.AddWithValue(parameter, jobId);
        }

        command.CommandText =
            $"""
             SELECT job_id, package_id, portable_job_id, definition_fingerprint,
                    source_job_id, imported_at
             FROM scheduled_job_imports
             WHERE job_id IN ({string.Join(", ", parameters)})
             ORDER BY imported_at
             """;
        return await ReadAsync(command, ct);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<ScheduledJobImportRecord>> GetActiveAsync(
        string packageId,
        string portableJobId,
        CancellationToken ct)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT i.job_id, i.package_id, i.portable_job_id, i.definition_fingerprint,
                   i.source_job_id, i.imported_at
            FROM scheduled_job_imports i
            INNER JOIN scheduled_jobs j ON j.id = i.job_id
            WHERE i.package_id = @package_id AND i.portable_job_id = @portable_job_id
            ORDER BY i.imported_at
            """;
        command.Parameters.AddWithValue("@package_id", packageId);
        command.Parameters.AddWithValue("@portable_job_id", portableJobId);

        return await ReadAsync(command, ct);
    }

    /// <inheritdoc />
    public async ValueTask AddAsync(
        ScheduledJobImportRecord record,
        CancellationToken ct)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO scheduled_job_imports (
                job_id, package_id, portable_job_id, definition_fingerprint, source_job_id, imported_at)
            VALUES (
                @job_id, @package_id, @portable_job_id, @definition_fingerprint, @source_job_id, @imported_at)
            """;
        command.Parameters.AddWithValue("@job_id", record.JobId);
        command.Parameters.AddWithValue("@package_id", record.PackageId);
        command.Parameters.AddWithValue("@portable_job_id", record.PortableJobId);
        command.Parameters.AddWithValue("@definition_fingerprint", record.DefinitionFingerprint);
        command.Parameters.AddWithValue("@source_job_id", (object?)record.SourceJobId ?? DBNull.Value);
        command.Parameters.AddWithValue("@imported_at", record.ImportedAt.ToString("o"));
        await command.ExecuteNonQueryAsync(ct);
    }

    /// <inheritdoc />
    public async ValueTask DeleteAsync(
        string jobId,
        CancellationToken ct)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM scheduled_job_imports WHERE job_id = @job_id";
        command.Parameters.AddWithValue("@job_id", jobId);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async ValueTask<IReadOnlyList<ScheduledJobImportRecord>> ReadAsync(
        SqliteCommand command,
        CancellationToken ct)
    {
        var result = new List<ScheduledJobImportRecord>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            result.Add(new ScheduledJobImportRecord(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                DateTimeOffset.Parse(reader.GetString(5))));
        }

        return result;
    }
}
