using Microsoft.Data.Sqlite;
using NexusLabs.Narnia.Core.Configuration;
using NexusLabs.Narnia.Core.Models;
using NexusLabs.Narnia.Core.Repositories;

namespace NexusLabs.Narnia.Core.Tests;

public sealed class SqliteScheduledJobRegistryTests : IDisposable
{
    private readonly SqliteConnection _keepAlive;
    private readonly SqliteScheduledJobRegistry _registry;
    private readonly DateTimeOffset _base = new(2026, 6, 28, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public SqliteScheduledJobRegistryTests()
    {
        var dbName = $"narnia_jobs_test_{Guid.NewGuid():N}";
        var connectionString = $"Data Source={dbName};Mode=Memory;Cache=Shared";

        _keepAlive = new SqliteConnection(connectionString);
        _keepAlive.Open();
        ApplyMigration("0007_add_scheduled_jobs.sql");
        ApplyMigration("0008_scheduled_jobs_definition.sql");

        _registry = new SqliteScheduledJobRegistry(new NarniaOptions
        {
            SettingsConnectionString = connectionString,
        });
    }

    public void Dispose() => _keepAlive.Dispose();

    private static ScheduledJobDraft Draft(
        string name,
        string taskName,
        string taskFolder = @"\Narnia\",
        params ScheduledJobSkill[] skills) =>
        new(
            Name: name,
            Description: "desc",
            Cwd: @"C:\dev\thing",
            Cadence: "Daily 05:00",
            Args: "-Lookback 24h",
            ScriptPath: @"C:\scripts\run.ps1",
            LogDir: @"C:\logs",
            AllowFlags: "--allow-all-tools --allow-all-paths",
            TaskFolder: taskFolder,
            TaskName: taskName,
            Notes: null,
            Skills: skills);

    private static ScheduledJobSkill Skill(string name, SkillResolution res = SkillResolution.Plugin) =>
        new(name, res, 0);

    [Fact]
    public async Task CreateAsync_PersistsJobWithMetadataAndSkills()
    {
        var created = await _registry.CreateAsync(
            Draft("Sample Radar Daily", "Narnia - Sample Radar Daily",
                skills: [Skill("example-issue-radar"), Skill("other", SkillResolution.RepoLocal)]),
            _base, Ct);

        Assert.Equal("Sample Radar Daily", created.Name);
        Assert.Equal(@"\Narnia\", created.TaskFolder);
        Assert.Equal(_base, created.CreatedAt);

        var fetched = await _registry.GetByIdAsync(created.Id, Ct);
        Assert.NotNull(fetched);
        Assert.Equal("Daily 05:00", fetched!.Cadence);
        Assert.Equal(@"C:\dev\thing", fetched.Cwd);
        Assert.Equal(["example-issue-radar", "other"], fetched.Skills.Select(s => s.Skill));
        Assert.Equal(SkillResolution.RepoLocal, fetched.Skills[1].Resolution);
        Assert.Equal([0, 1], fetched.Skills.Select(s => s.Order));
    }

    [Fact]
    public async Task CreateAsync_DuplicateAndBlankSkills_AreCollapsed()
    {
        var created = await _registry.CreateAsync(
            Draft("J", "T", skills:
            [
                Skill("a"), Skill("a", SkillResolution.RepoLocal), new ScheduledJobSkill("  ", SkillResolution.Unknown, 0), Skill("b"),
            ]),
            _base, Ct);

        Assert.Equal(["a", "b"], created.Skills.Select(s => s.Skill));
        Assert.Equal([0, 1], created.Skills.Select(s => s.Order));
    }

    [Fact]
    public async Task GetAllAsync_OrdersByUpdatedAtDescending()
    {
        await _registry.CreateAsync(Draft("First", "T1"), _base, Ct);
        await _registry.CreateAsync(Draft("Second", "T2"), _base.AddMinutes(5), Ct);

        var all = await _registry.GetAllAsync(Ct);

        Assert.Equal(["Second", "First"], all.Select(j => j.Name));
    }

    [Fact]
    public async Task UpdateAsync_ReplacesMetadataAndSkills_AndTouchesUpdatedAt()
    {
        var created = await _registry.CreateAsync(Draft("Old", "T", skills: [Skill("a")]), _base, Ct);

        var updated = Draft("New", "T2", taskFolder: @"\Narnia\", skills: [Skill("b"), Skill("c")])
            with { Cadence = "Weekly Fri 05:30" };
        await _registry.UpdateAsync(created.Id, updated, _base.AddMinutes(10), Ct);

        var fetched = await _registry.GetByIdAsync(created.Id, Ct);
        Assert.Equal("New", fetched!.Name);
        Assert.Equal("T2", fetched.TaskName);
        Assert.Equal("Weekly Fri 05:30", fetched.Cadence);
        Assert.Equal(["b", "c"], fetched.Skills.Select(s => s.Skill));
        Assert.Equal(_base.AddMinutes(10), fetched.UpdatedAt);
        Assert.Equal(_base, fetched.CreatedAt);
    }

    [Fact]
    public async Task UpdateAsync_UnknownId_DoesNothing()
    {
        await _registry.UpdateAsync(Guid.NewGuid().ToString(), Draft("X", "T"), _base, Ct);
        Assert.Empty(await _registry.GetAllAsync(Ct));
    }

    [Fact]
    public async Task DeleteAsync_RemovesJobAndSkills()
    {
        var created = await _registry.CreateAsync(Draft("Doomed", "T", skills: [Skill("a"), Skill("b")]), _base, Ct);

        await _registry.DeleteAsync(created.Id, Ct);

        Assert.Null(await _registry.GetByIdAsync(created.Id, Ct));
        Assert.Equal(0, await CountSkillsAsync(created.Id));
    }

    [Fact]
    public async Task GetByIdAsync_UnknownId_ReturnsNull()
    {
        Assert.Null(await _registry.GetByIdAsync(Guid.NewGuid().ToString(), Ct));
    }

    [Fact]
    public async Task CreateWithIdAsync_UsesGivenId_AndRoundTripsShapeBFields()
    {
        var id = Guid.NewGuid().ToString();
        var draft = Draft("Sample", "Narnia - Sample") with
        {
            Prompt = "Run the sample radar",
            CadenceKind = "Weekly",
            CadenceTime = "06:30",
            CadenceDays = "Monday,Friday",
            CopilotArgs = "--model gpt-5",
            ScriptPath = @"C:\narnia\schedules\x\run.ps1",
            LogDir = @"C:\narnia\schedules\x\logs",
        };

        var created = await _registry.CreateWithIdAsync(id, draft, _base, Ct);
        Assert.Equal(id, created.Id);

        var fetched = await _registry.GetByIdAsync(id, Ct);
        Assert.Equal("Run the sample radar", fetched!.Prompt);
        Assert.Equal("Weekly", fetched.CadenceKind);
        Assert.Equal("06:30", fetched.CadenceTime);
        Assert.Equal("Monday,Friday", fetched.CadenceDays);
        Assert.Equal("--model gpt-5", fetched.CopilotArgs);
    }

    [Fact]
    public async Task UpdateAsync_ChangesShapeBFields()
    {
        var created = await _registry.CreateWithIdAsync(
            Guid.NewGuid().ToString(),
            Draft("J", "T") with { Prompt = "old" },
            _base, Ct);

        await _registry.UpdateAsync(created.Id, Draft("J", "T") with { Prompt = "new", CadenceKind = "Daily" }, _base.AddMinutes(1), Ct);

        var fetched = await _registry.GetByIdAsync(created.Id, Ct);
        Assert.Equal("new", fetched!.Prompt);
        Assert.Equal("Daily", fetched.CadenceKind);
    }

    [Fact]
    public async Task NullableMetadata_RoundTripsAsNull()
    {
        var draft = new ScheduledJobDraft(
            Name: "Minimal", Description: null, Cwd: null, Cadence: null, Args: null, ScriptPath: null,
            LogDir: null, AllowFlags: null, TaskFolder: @"\Narnia\", TaskName: "Min",
            Notes: null, Skills: []);

        var created = await _registry.CreateAsync(draft, _base, Ct);
        var fetched = await _registry.GetByIdAsync(created.Id, Ct);

        Assert.NotNull(fetched);
        Assert.Null(fetched!.Cwd);
        Assert.Null(fetched.Cadence);
        Assert.Null(fetched.LogDir);
        Assert.Empty(fetched.Skills);
    }

    private async Task<long> CountSkillsAsync(string jobId)
    {
        await using var cmd = _keepAlive.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM scheduled_job_skills WHERE job_id = @id";
        cmd.Parameters.AddWithValue("@id", jobId);
        return (long)(await cmd.ExecuteScalarAsync(Ct))!;
    }

    private void ApplyMigration(string fileName)
    {
        var assembly = typeof(SqliteScheduledJobRegistry).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith(fileName, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);

        using var cmd = _keepAlive.CreateCommand();
        cmd.CommandText = reader.ReadToEnd();
        cmd.ExecuteNonQuery();
    }
}
