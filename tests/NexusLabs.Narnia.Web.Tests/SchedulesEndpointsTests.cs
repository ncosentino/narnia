using System.Net.Http.Json;
using NexusLabs.Narnia.Core.Models;
using NexusLabs.Narnia.Core.Services;

namespace NexusLabs.Narnia.Web.Tests;

public sealed class SchedulesEndpointsTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 28, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ScheduledJobDraft Draft(string name, string taskName, string taskFolder = @"\Narnia\") =>
        new(
            Name: name, Description: null, Cwd: @"C:\dev\x", Cadence: "Daily 05:00", Args: "-Lookback 24h",
            ScriptPath: @"C:\s\run.ps1", LogDir: @"C:\logs", AllowFlags: "--allow-all-tools",
            TaskFolder: taskFolder, TaskName: taskName, Notes: null,
            Skills: [new ScheduledJobSkill("example-issue-radar", SkillResolution.Plugin, 0)]);

    private static ScheduledTaskStatus Status(
        string folder, string name, int? lastResult = 0) =>
        new(folder, name, ScheduledTaskState.Ready, Now.AddDays(-1), lastResult, Now.AddDays(1),
            "powershell.exe -File run.ps1");

    [Fact]
    public async Task GetSchedules_JoinsCatalogToLiveStatus()
    {
        using var factory = new NarniaWebAppFactory();
        var created = await factory.ScheduledJobRegistry.CreateAsync(
            Draft("Sample Daily", "Narnia - Sample Daily"), Now, Ct);

        factory.ScheduledTaskProvider
            .Setup(p => p.ListInFolderAsync(@"\Narnia\", It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ScheduledTaskStatus>)[Status(@"\Narnia\", "Narnia - Sample Daily")]);

        var client = factory.CreateClient();
        var response = await client.GetFromJsonAsync<SchedulesResponse>("/api/schedules", Ct);

        Assert.NotNull(response);
        Assert.True(response!.SchedulerSupported);
        var job = Assert.Single(response.Jobs);
        Assert.Equal(created.Id, job.Id);
        Assert.True(job.TaskFound);
        Assert.NotNull(job.Status);
        Assert.Equal(0, job.Status!.LastResult);
        Assert.Equal("succeeded", job.Health);
        Assert.False(job.RequiresAttention);
        Assert.Equal("ready", job.Status.State);
        Assert.Empty(response.Untracked);
        var skill = Assert.Single(job.Skills);
        Assert.Equal("plugin", skill.Resolution);
    }

    [Fact]
    public async Task GetSchedules_FlagsDrift_WhenTaskMissing()
    {
        using var factory = new NarniaWebAppFactory();
        await factory.ScheduledJobRegistry.CreateAsync(Draft("Gone", "Narnia - Gone"), Now, Ct);
        // Provider returns no tasks → job has no matching live task.

        var client = factory.CreateClient();
        var response = await client.GetFromJsonAsync<SchedulesResponse>("/api/schedules", Ct);

        var job = Assert.Single(response!.Jobs);
        Assert.False(job.TaskFound);
        Assert.Null(job.Status);
        Assert.Equal("drift", job.Health);
        Assert.True(job.RequiresAttention);
    }

    [Fact]
    public async Task GetSchedules_SurfacesUntrackedNarniaTasks()
    {
        using var factory = new NarniaWebAppFactory();
        factory.ScheduledTaskProvider
            .Setup(p => p.ListInFolderAsync(@"\Narnia\", It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ScheduledTaskStatus>)[Status(@"\Narnia\", "Hand-made", lastResult: 1)]);

        var client = factory.CreateClient();
        var response = await client.GetFromJsonAsync<SchedulesResponse>("/api/schedules", Ct);

        Assert.Empty(response!.Jobs);
        var untracked = Assert.Single(response.Untracked);
        Assert.Equal("Hand-made", untracked.TaskName);
        Assert.Equal(1, untracked.LastResult);
    }

    [Fact]
    public async Task SchedulesPage_UsesSharedHealthClassification()
    {
        using var factory = new NarniaWebAppFactory();
        await factory.ScheduledJobRegistry.CreateAsync(
            Draft("Failing job", "Narnia - Failing job"), Now, Ct);
        factory.ScheduledTaskProvider
            .Setup(p => p.ListInFolderAsync(@"\Narnia\", It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ScheduledTaskStatus>)
            [
                Status(@"\Narnia\", "Narnia - Failing job", lastResult: 1),
            ]);

        var client = factory.CreateClient();
        var html = await client.GetStringAsync("/schedules", Ct);

        Assert.Contains("Failing job", html, StringComparison.Ordinal);
        Assert.Contains("failed (0x1)", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetSchedules_AdoptedJobOutsideNarniaFolder_ResolvedViaGet()
    {
        using var factory = new NarniaWebAppFactory();
        await factory.ScheduledJobRegistry.CreateAsync(
            Draft("Adopted", "External - Sample Radar Daily", taskFolder: @"\External\"), Now, Ct);

        factory.ScheduledTaskProvider
            .Setup(p => p.GetAsync(@"\External\", "External - Sample Radar Daily", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Status(@"\External\", "External - Sample Radar Daily"));

        var client = factory.CreateClient();
        var response = await client.GetFromJsonAsync<SchedulesResponse>("/api/schedules", Ct);

        var job = Assert.Single(response!.Jobs);
        Assert.True(job.TaskFound);
        Assert.Empty(response.Untracked);
        factory.ScheduledTaskProvider.Verify(
            p => p.GetAsync(@"\External\", "External - Sample Radar Daily", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetSchedules_ReportsSchedulerUnsupported()
    {
        using var factory = new NarniaWebAppFactory();
        factory.ScheduledTaskProvider.SetupGet(p => p.IsSupported).Returns(false);

        var client = factory.CreateClient();
        var response = await client.GetFromJsonAsync<SchedulesResponse>("/api/schedules", Ct);

        Assert.False(response!.SchedulerSupported);
    }

    [Fact]
    public async Task CreateRegister_RegistersTaskAndCatalogsJob()
    {
        using var factory = new NarniaWebAppFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/schedules", new
        {
            name = "Sample Daily", prompt = "Run example-issue-radar with --lookback 24h",
            cadenceKind = "daily", time = "05:00", register = true,
        }, Ct);

        response.EnsureSuccessStatusCode();
        var job = Assert.Single(await factory.ScheduledJobRegistry.GetAllAsync(Ct));
        Assert.Equal("Run example-issue-radar with --lookback 24h", job.Prompt);
        Assert.Equal("daily", job.CadenceKind, ignoreCase: true);
        factory.ScheduledJobWorkspace.Verify(w => w.WriteScriptAsync(job.Id, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        factory.ScheduledTaskRegistrar.Verify(
            r => r.RegisterAsync(It.IsAny<ScheduledTaskRegistration>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Create_MissingPrompt_Returns400()
    {
        using var factory = new NarniaWebAppFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/schedules", new
        {
            name = "No prompt", cadenceKind = "daily", time = "05:00", register = true,
        }, Ct);

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await factory.ScheduledJobRegistry.GetAllAsync(Ct));
    }

    [Fact]
    public async Task CreateCopyPaste_ReturnsScriptAndCommand_AndCatalogsNothing()
    {
        using var factory = new NarniaWebAppFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/schedules", new
        {
            name = "Sample Daily", prompt = "Run the sample radar", cadenceKind = "daily", time = "05:00", register = false,
        }, Ct);

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<CreateResponse>(Ct);
        Assert.False(body!.Registered);
        Assert.Contains("Register-ScheduledTask", body.Command);
        Assert.Contains("copilot -p", body.Script);
        Assert.Empty(await factory.ScheduledJobRegistry.GetAllAsync(Ct));
        factory.ScheduledTaskRegistrar.Verify(
            r => r.RegisterAsync(It.IsAny<ScheduledTaskRegistration>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateRegister_RollsBackCatalogAndWorkspace_WhenRegistrarFails()
    {
        using var factory = new NarniaWebAppFactory();
        factory.ScheduledTaskRegistrar
            .Setup(r => r.RegisterAsync(It.IsAny<ScheduledTaskRegistration>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ScheduledTaskCommandResult.Fail("nope"));
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/schedules", new
        {
            name = "Bad", prompt = "x", cadenceKind = "daily", time = "05:00", register = true,
        }, Ct);

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await factory.ScheduledJobRegistry.GetAllAsync(Ct));
        factory.ScheduledJobWorkspace.Verify(w => w.Delete(It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task Update_RegeneratesScript_ReRegisters_AndUpdatesCatalog()
    {
        using var factory = new NarniaWebAppFactory();
        var client = factory.CreateClient();
        var create = await client.PostAsJsonAsync("/api/schedules", new
        {
            name = "Sample", prompt = "old prompt", cadenceKind = "daily", time = "05:00", register = true,
        }, Ct);
        var id = (await create.Content.ReadFromJsonAsync<CreateResponse>(Ct))!.Id;

        var update = await client.PutAsJsonAsync($"/api/schedules/{id}", new
        {
            name = "Sample", prompt = "new prompt", cadenceKind = "weekly", time = "06:30", days = new[] { "Friday" }, register = true,
        }, Ct);

        update.EnsureSuccessStatusCode();
        var job = await factory.ScheduledJobRegistry.GetByIdAsync(id, Ct);
        Assert.Equal("new prompt", job!.Prompt);
        Assert.Equal("weekly", job.CadenceKind, ignoreCase: true);
        factory.ScheduledJobWorkspace.Verify(w => w.WriteScriptAsync(id, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        factory.ScheduledTaskRegistrar.Verify(r => r.RegisterAsync(It.IsAny<ScheduledTaskRegistration>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Update_AnyJob_SucceedsAndRegeneratesScript()
    {
        // There is one job format: every job is editable and, on save, is (re)generated as a
        // first-class Narnia job with its own wrapper script and re-registered task.
        using var factory = new NarniaWebAppFactory();
        var job = await factory.ScheduledJobRegistry.CreateAsync(Draft("Seeded", "Narnia - Seeded"), Now, Ct);
        var client = factory.CreateClient();

        var response = await client.PutAsJsonAsync($"/api/schedules/{job.Id}", new
        {
            name = "Seeded", prompt = "now has a prompt", cadenceKind = "daily", time = "05:00", register = true,
        }, Ct);

        response.EnsureSuccessStatusCode();
        var updated = await factory.ScheduledJobRegistry.GetByIdAsync(job.Id, Ct);
        Assert.Equal("now has a prompt", updated!.Prompt);
        factory.ScheduledJobWorkspace.Verify(w => w.WriteScriptAsync(job.Id, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        factory.ScheduledTaskRegistrar.Verify(r => r.RegisterAsync(It.IsAny<ScheduledTaskRegistration>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Update_WithMultipleSkills_PersistsAllOfThemInOrder()
    {
        // Regression test: the web UI's edit form used to round-trip only the first skill,
        // so saving an update silently truncated a job with several skills down to one. The
        // fix now resubmits every skill on save; this locks in that the backend persists an
        // arbitrary-length skill list rather than just the first entry.
        using var factory = new NarniaWebAppFactory();
        var job = await factory.ScheduledJobRegistry.CreateAsync(Draft("Linker", "Narnia - Linker"), Now, Ct);
        var client = factory.CreateClient();

        var response = await client.PutAsJsonAsync($"/api/schedules/{job.Id}", new
        {
            name = "Linker",
            prompt = "run the pipeline",
            cadenceKind = "weekly",
            time = "02:00",
            days = new[] { "Saturday" },
            register = true,
            skills = new[]
            {
                new { skill = "example-link-pipeline", resolution = "plugin" },
                new { skill = "example-link-scheduled", resolution = "repolocal" },
                new { skill = "example-link-discovery", resolution = "repolocal" },
            },
        }, Ct);

        response.EnsureSuccessStatusCode();
        var updated = await factory.ScheduledJobRegistry.GetByIdAsync(job.Id, Ct);
        Assert.Equal(
            ["example-link-pipeline", "example-link-scheduled", "example-link-discovery"],
            updated!.Skills.OrderBy(s => s.Order).Select(s => s.Skill));
        Assert.Equal(
            SkillResolution.RepoLocal,
            updated.Skills.Single(s => s.Skill == "example-link-discovery").Resolution);
    }

    [Fact]
    public async Task Delete_OwnedJob_RemovesTaskAndWorkspace()
    {
        using var factory = new NarniaWebAppFactory();
        var client = factory.CreateClient();
        var create = await client.PostAsJsonAsync("/api/schedules", new
        {
            name = "Sample", prompt = "p", cadenceKind = "daily", time = "05:00", register = true,
        }, Ct);
        var id = (await create.Content.ReadFromJsonAsync<CreateResponse>(Ct))!.Id;

        var response = await client.DeleteAsync($"/api/schedules/{id}", Ct);

        Assert.Equal(System.Net.HttpStatusCode.NoContent, response.StatusCode);
        Assert.Null(await factory.ScheduledJobRegistry.GetByIdAsync(id, Ct));
        factory.ScheduledTaskRegistrar.Verify(r => r.DeleteAsync(@"\Narnia\", "Sample", It.IsAny<CancellationToken>()), Times.Once);
        factory.ScheduledJobWorkspace.Verify(w => w.Delete(id), Times.Once);
    }

    [Fact]
    public async Task RunNow_StartsTask()
    {
        using var factory = new NarniaWebAppFactory();
        var job = await factory.ScheduledJobRegistry.CreateAsync(Draft("J", "Narnia - J"), Now, Ct);
        var client = factory.CreateClient();

        var response = await client.PostAsync($"/api/schedules/{job.Id}/run", null, Ct);

        response.EnsureSuccessStatusCode();
        factory.ScheduledTaskRegistrar.Verify(r => r.RunAsync(@"\Narnia\", "Narnia - J", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Delete_AlwaysRemovesTaskAndWorkspace()
    {
        using var factory = new NarniaWebAppFactory();
        var job = await factory.ScheduledJobRegistry.CreateAsync(Draft("J", "Narnia - J"), Now, Ct);
        var client = factory.CreateClient();

        var response = await client.DeleteAsync($"/api/schedules/{job.Id}", Ct);

        Assert.Equal(System.Net.HttpStatusCode.NoContent, response.StatusCode);
        Assert.Null(await factory.ScheduledJobRegistry.GetByIdAsync(job.Id, Ct));
        factory.ScheduledTaskRegistrar.Verify(r => r.DeleteAsync(@"\Narnia\", "Narnia - J", It.IsAny<CancellationToken>()), Times.Once);
        factory.ScheduledJobWorkspace.Verify(w => w.Delete(job.Id), Times.Once);
    }

    private sealed record CreateResponse(bool Registered, string Id, string Command, string Script);

    private sealed record SchedulesResponse(bool SchedulerSupported, List<JobDto> Jobs, List<StatusDto> Untracked);

    private sealed record JobDto(
        string Id,
        string Name,
        bool TaskFound,
        List<SkillDto> Skills,
        StatusDto? Status,
        string Health,
        bool RequiresAttention);

    private sealed record SkillDto(string Skill, string Resolution);

    private sealed record StatusDto(string TaskName, string State, int? LastResult);
}
