using Microsoft.Data.Sqlite;
using NexusLabs.Narnia.Core.Configuration;
using NexusLabs.Narnia.Core.Models;

namespace NexusLabs.Narnia.Core.Repositories;

public sealed class SqliteScheduledJobRegistry(NarniaOptions options) : IScheduledJobRegistry
{
    private const string JobColumns =
        "id, name, description, cwd, cadence, args, script_path, log_dir, allow_flags, task_folder, task_name, notes, created_at, updated_at, prompt, cadence_kind, cadence_time, cadence_days, copilot_args";

    private readonly string _connectionString = options.SettingsConnectionString
        ?? $"Data Source={options.SettingsDatabasePath}";

    public async ValueTask<IReadOnlyList<ScheduledJob>> GetAllAsync(CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {JobColumns} FROM scheduled_jobs ORDER BY updated_at DESC";
        return await ReadJobsAsync(conn, cmd, ct);
    }

    public async ValueTask<ScheduledJob?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {JobColumns} FROM scheduled_jobs WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);

        var jobs = await ReadJobsAsync(conn, cmd, ct);
        return jobs.Count > 0 ? jobs[0] : null;
    }

    public ValueTask<ScheduledJob> CreateAsync(
        ScheduledJobDraft draft,
        DateTimeOffset now,
        CancellationToken ct = default) =>
        CreateWithIdAsync(Guid.NewGuid().ToString(), draft, now, ct);

    public async ValueTask<ScheduledJob> CreateWithIdAsync(
        string id,
        ScheduledJobDraft draft,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        var skills = NormalizeSkills(draft.Skills);

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(ct);

        await using (var insert = conn.CreateCommand())
        {
            insert.Transaction = tx;
            insert.CommandText =
                $"""
                INSERT INTO scheduled_jobs ({JobColumns})
                VALUES (@id, @name, @description, @cwd, @cadence, @args, @script_path, @log_dir, @allow_flags, @task_folder, @task_name, @notes, @now, @now, @prompt, @cadence_kind, @cadence_time, @cadence_days, @copilot_args)
                """;
            BindDraft(insert, draft);
            insert.Parameters.AddWithValue("@id", id);
            insert.Parameters.AddWithValue("@now", now.ToString("o"));
            await insert.ExecuteNonQueryAsync(ct);
        }

        await InsertSkillsAsync(conn, tx, id, skills, ct);
        await tx.CommitAsync(ct);

        return new ScheduledJob(
            id, draft.Name, draft.Description, draft.Cwd, draft.Cadence, draft.Args, draft.ScriptPath,
            draft.LogDir, draft.AllowFlags, draft.TaskFolder, draft.TaskName,
            draft.Notes, now, now, skills,
            draft.Prompt, draft.CadenceKind, draft.CadenceTime, draft.CadenceDays, draft.CopilotArgs);
    }

    public async ValueTask UpdateAsync(
        string id,
        ScheduledJobDraft draft,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        var skills = NormalizeSkills(draft.Skills);

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(ct);

        long affected;
        await using (var update = conn.CreateCommand())
        {
            update.Transaction = tx;
            update.CommandText =
                """
                UPDATE scheduled_jobs SET
                    name = @name, description = @description, cwd = @cwd, cadence = @cadence,
                    args = @args, script_path = @script_path, log_dir = @log_dir, allow_flags = @allow_flags,
                    task_folder = @task_folder, task_name = @task_name,
                    notes = @notes, updated_at = @now, prompt = @prompt, cadence_kind = @cadence_kind,
                    cadence_time = @cadence_time, cadence_days = @cadence_days, copilot_args = @copilot_args
                WHERE id = @id
                """;
            BindDraft(update, draft);
            update.Parameters.AddWithValue("@id", id);
            update.Parameters.AddWithValue("@now", now.ToString("o"));
            affected = await update.ExecuteNonQueryAsync(ct);
        }

        if (affected == 0)
        {
            await tx.RollbackAsync(ct);
            return;
        }

        await DeleteSkillsAsync(conn, tx, id, ct);
        await InsertSkillsAsync(conn, tx, id, skills, ct);
        await tx.CommitAsync(ct);
    }

    public async ValueTask DeleteAsync(string id, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(ct);

        await DeleteSkillsAsync(conn, tx, id, ct);
        await using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM scheduled_jobs WHERE id = @id";
            cmd.Parameters.AddWithValue("@id", id);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
    }

    private static void BindDraft(SqliteCommand cmd, ScheduledJobDraft draft)
    {
        cmd.Parameters.AddWithValue("@name", draft.Name);
        cmd.Parameters.AddWithValue("@description", (object?)draft.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@cwd", (object?)draft.Cwd ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@cadence", (object?)draft.Cadence ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@args", (object?)draft.Args ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@script_path", (object?)draft.ScriptPath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@log_dir", (object?)draft.LogDir ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@allow_flags", (object?)draft.AllowFlags ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@task_folder", draft.TaskFolder);
        cmd.Parameters.AddWithValue("@task_name", draft.TaskName);
        cmd.Parameters.AddWithValue("@notes", (object?)draft.Notes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@prompt", (object?)draft.Prompt ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@cadence_kind", (object?)draft.CadenceKind ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@cadence_time", (object?)draft.CadenceTime ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@cadence_days", (object?)draft.CadenceDays ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@copilot_args", (object?)draft.CopilotArgs ?? DBNull.Value);
    }

    // Collapses duplicate skill names to their first occurrence and assigns sequential order.
    private static List<ScheduledJobSkill> NormalizeSkills(IReadOnlyList<ScheduledJobSkill> skills)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<ScheduledJobSkill>(skills.Count);
        foreach (var skill in skills)
        {
            if (string.IsNullOrWhiteSpace(skill.Skill) || !seen.Add(skill.Skill))
                continue;
            result.Add(skill with { Order = result.Count });
        }

        return result;
    }

    private static async ValueTask InsertSkillsAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        string jobId,
        IReadOnlyList<ScheduledJobSkill> skills,
        CancellationToken ct)
    {
        foreach (var skill in skills)
        {
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText =
                "INSERT INTO scheduled_job_skills (job_id, skill, resolution, skill_order) VALUES (@j, @s, @r, @o)";
            cmd.Parameters.AddWithValue("@j", jobId);
            cmd.Parameters.AddWithValue("@s", skill.Skill);
            cmd.Parameters.AddWithValue("@r", skill.Resolution.ToString());
            cmd.Parameters.AddWithValue("@o", skill.Order);
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    private static async ValueTask DeleteSkillsAsync(
        SqliteConnection conn, SqliteTransaction tx, string jobId, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "DELETE FROM scheduled_job_skills WHERE job_id = @id";
        cmd.Parameters.AddWithValue("@id", jobId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async ValueTask<IReadOnlyList<ScheduledJob>> ReadJobsAsync(
        SqliteConnection conn, SqliteCommand jobsCommand, CancellationToken ct)
    {
        var rows = new List<ScheduledJobRow>();
        await using (var reader = await jobsCommand.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
                rows.Add(ReadJobRow(reader));
        }

        if (rows.Count == 0)
            return [];

        var skillsByJob = await LoadSkillsAsync(conn, rows.Select(r => r.Id).ToList(), ct);

        var result = new List<ScheduledJob>(rows.Count);
        foreach (var row in rows)
        {
            var skills = skillsByJob.TryGetValue(row.Id, out var s) ? s : [];
            result.Add(row.ToJob(skills));
        }

        return result;
    }

    private static async ValueTask<Dictionary<string, List<ScheduledJobSkill>>> LoadSkillsAsync(
        SqliteConnection conn, IReadOnlyList<string> jobIds, CancellationToken ct)
    {
        var result = new Dictionary<string, List<ScheduledJobSkill>>(StringComparer.Ordinal);
        if (jobIds.Count == 0)
            return result;

        await using var cmd = conn.CreateCommand();
        var parameters = new List<string>(jobIds.Count);
        for (var i = 0; i < jobIds.Count; i++)
        {
            var name = $"@j{i}";
            parameters.Add(name);
            cmd.Parameters.AddWithValue(name, jobIds[i]);
        }

        cmd.CommandText =
            $"SELECT job_id, skill, resolution, skill_order FROM scheduled_job_skills WHERE job_id IN ({string.Join(", ", parameters)}) ORDER BY skill_order";

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var jobId = reader.GetString(0);
            var skill = new ScheduledJobSkill(
                reader.GetString(1),
                ParseResolution(reader.IsDBNull(2) ? null : reader.GetString(2)),
                reader.GetInt32(3));

            if (!result.TryGetValue(jobId, out var list))
            {
                list = [];
                result[jobId] = list;
            }

            list.Add(skill);
        }

        return result;
    }

    private static ScheduledJobRow ReadJobRow(SqliteDataReader reader) => new(
        reader.GetString(0),
        reader.GetString(1),
        reader.IsDBNull(2) ? null : reader.GetString(2),
        reader.IsDBNull(3) ? null : reader.GetString(3),
        reader.IsDBNull(4) ? null : reader.GetString(4),
        reader.IsDBNull(5) ? null : reader.GetString(5),
        reader.IsDBNull(6) ? null : reader.GetString(6),
        reader.IsDBNull(7) ? null : reader.GetString(7),
        reader.IsDBNull(8) ? null : reader.GetString(8),
        reader.GetString(9),
        reader.GetString(10),
        reader.IsDBNull(11) ? null : reader.GetString(11),
        ParseTimestamp(reader.GetString(12)),
        ParseTimestamp(reader.GetString(13)),
        reader.IsDBNull(14) ? null : reader.GetString(14),
        reader.IsDBNull(15) ? null : reader.GetString(15),
        reader.IsDBNull(16) ? null : reader.GetString(16),
        reader.IsDBNull(17) ? null : reader.GetString(17),
        reader.IsDBNull(18) ? null : reader.GetString(18));

    private static SkillResolution ParseResolution(string? value) =>
        Enum.TryParse<SkillResolution>(value, out var r) ? r : SkillResolution.Unknown;

    private static DateTimeOffset ParseTimestamp(string value) =>
        DateTimeOffset.TryParse(value, out var dt) ? dt : DateTimeOffset.MinValue;

    private sealed record ScheduledJobRow(
        string Id,
        string Name,
        string? Description,
        string? Cwd,
        string? Cadence,
        string? Args,
        string? ScriptPath,
        string? LogDir,
        string? AllowFlags,
        string TaskFolder,
        string TaskName,
        string? Notes,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        string? Prompt,
        string? CadenceKind,
        string? CadenceTime,
        string? CadenceDays,
        string? CopilotArgs)
    {
        public ScheduledJob ToJob(IReadOnlyList<ScheduledJobSkill> skills) => new(
            Id, Name, Description, Cwd, Cadence, Args, ScriptPath, LogDir, AllowFlags, TaskFolder,
            TaskName, Notes, CreatedAt, UpdatedAt, skills,
            Prompt, CadenceKind, CadenceTime, CadenceDays, CopilotArgs);
    }
}
