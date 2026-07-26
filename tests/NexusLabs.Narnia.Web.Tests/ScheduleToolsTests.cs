using System.Text.Json;
using NexusLabs.Narnia.Core.Models;
using NexusLabs.Narnia.Core.Services;
using NexusLabs.Narnia.Web.Mcp;

namespace NexusLabs.Narnia.Web.Tests;

public sealed class ScheduleToolsTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 28, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly Mock<IScheduledJobService> _jobService = new();

    private ScheduleTools CreateTools() => new(_jobService.Object);

    private static ScheduledJob Job(string id = "job-1") => new(
        id, "Sample", "desc", @"C:\dev\x", "Daily 05:00", null, @"C:\narnia\run.ps1", @"C:\narnia\logs",
        "--allow-all-tools", @"\Narnia\", "Narnia - Sample", null, Now, Now,
        [new ScheduledJobSkill("example-skill", SkillResolution.Plugin, 0)],
        Prompt: "do the thing", CadenceKind: "Daily", CadenceTime: "05:00");

    // ── list_schedules ───────────────────────────────────────────────────────

    [Fact]
    public async Task ListSchedulesAsync_ReturnsJobsJoinedToStatusAndUntracked()
    {
        var status = new ScheduledTaskStatus(@"\Narnia\", "Narnia - Sample", ScheduledTaskState.Ready, null, 0, Now.AddDays(1), "powershell.exe");
        var untracked = new ScheduledTaskStatus(@"\Narnia\", "Hand-made", ScheduledTaskState.Disabled, null, null, null, null);
        _jobService.Setup(s => s.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
            new ScheduledJobListView(true, [new ScheduledJobStatusView(Job(), status, true)], [untracked]));
        var tools = CreateTools();

        var json = await tools.ListSchedulesAsync(Ct);
        using var doc = JsonDocument.Parse(json);

        Assert.True(doc.RootElement.GetProperty("schedulerSupported").GetBoolean());
        var jobs = doc.RootElement.GetProperty("jobs");
        Assert.Equal(1, jobs.GetArrayLength());
        var jobEntry = jobs[0];
        Assert.Equal("job-1", jobEntry.GetProperty("job").GetProperty("id").GetString());
        Assert.True(jobEntry.GetProperty("taskFound").GetBoolean());
        Assert.Equal("ready", jobEntry.GetProperty("status").GetProperty("state").GetString());
        var skill = jobEntry.GetProperty("job").GetProperty("skills")[0];
        Assert.Equal("plugin", skill.GetProperty("resolution").GetString());
        var untrackedEntry = doc.RootElement.GetProperty("untracked")[0];
        Assert.Equal("Hand-made", untrackedEntry.GetProperty("taskName").GetString());
    }

    [Fact]
    public async Task ListSchedulesAsync_ServiceThrows_ReturnsErrorString()
    {
        _jobService.Setup(s => s.ListAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("boom"));
        var tools = CreateTools();

        var result = await tools.ListSchedulesAsync(Ct);

        Assert.StartsWith("Error:", result);
        Assert.Contains("boom", result);
    }

    // ── get_schedule ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetScheduleAsync_Found_ReturnsJobJson()
    {
        _jobService.Setup(s => s.GetAsync("job-1", It.IsAny<CancellationToken>())).ReturnsAsync(Job());
        var tools = CreateTools();

        var json = await tools.GetScheduleAsync("job-1", Ct);
        using var doc = JsonDocument.Parse(json);

        Assert.Equal("job-1", doc.RootElement.GetProperty("id").GetString());
        Assert.Equal("do the thing", doc.RootElement.GetProperty("prompt").GetString());
    }

    [Fact]
    public async Task GetScheduleAsync_NotFound_ReturnsErrorString()
    {
        _jobService.Setup(s => s.GetAsync("missing", It.IsAny<CancellationToken>())).ReturnsAsync((ScheduledJob?)null);
        var tools = CreateTools();

        var result = await tools.GetScheduleAsync("missing", Ct);

        Assert.StartsWith("Error:", result);
        Assert.Contains("missing", result);
    }

    // ── create_schedule ──────────────────────────────────────────────────────

    [Fact]
    public async Task CreateScheduleAsync_Registered_ReturnsJobId()
    {
        _jobService
            .Setup(s => s.CreateAsync(It.IsAny<ScheduledJobInput>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ScheduledJobCreateResult.Created(Job()));
        var tools = CreateTools();

        var json = await tools.CreateScheduleAsync(name: "Sample", prompt: "do the thing", register: true, cancellationToken: Ct);
        using var doc = JsonDocument.Parse(json);

        Assert.True(doc.RootElement.GetProperty("registered").GetBoolean());
        Assert.Equal("job-1", doc.RootElement.GetProperty("id").GetString());
    }

    [Fact]
    public async Task CreateScheduleAsync_CopyPaste_ReturnsScriptAndCommand()
    {
        _jobService
            .Setup(s => s.CreateAsync(It.IsAny<ScheduledJobInput>(), false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ScheduledJobCreateResult.CopyPaste("script-body", "Register-ScheduledTask ..."));
        var tools = CreateTools();

        var json = await tools.CreateScheduleAsync(name: "Sample", prompt: "do the thing", register: false, cancellationToken: Ct);
        using var doc = JsonDocument.Parse(json);

        Assert.False(doc.RootElement.GetProperty("registered").GetBoolean());
        Assert.Equal("script-body", doc.RootElement.GetProperty("script").GetString());
        Assert.Equal("Register-ScheduledTask ...", doc.RootElement.GetProperty("command").GetString());
    }

    [Fact]
    public async Task CreateScheduleAsync_ServiceFailure_ReturnsErrorString()
    {
        _jobService
            .Setup(s => s.CreateAsync(It.IsAny<ScheduledJobInput>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ScheduledJobCreateResult.Failure("A prompt is required."));
        var tools = CreateTools();

        var result = await tools.CreateScheduleAsync(name: "Sample", prompt: "", cancellationToken: Ct);

        Assert.Equal("Error: A prompt is required.", result);
    }

    [Fact]
    public async Task CreateScheduleAsync_PassesSkillsThrough()
    {
        ScheduledJobInput? captured = null;
        _jobService
            .Setup(s => s.CreateAsync(It.IsAny<ScheduledJobInput>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback<ScheduledJobInput, bool, CancellationToken>((input, _, _) => captured = input)
            .ReturnsAsync(ScheduledJobCreateResult.Created(Job()));
        var tools = CreateTools();

        await tools.CreateScheduleAsync(
            name: "Sample", prompt: "do the thing",
            skills: [new ScheduleSkillMcpInput("example-skill", "plugin")], cancellationToken: Ct);

        Assert.NotNull(captured);
        var skill = Assert.Single(captured!.Skills!);
        Assert.Equal("example-skill", skill.Skill);
        Assert.Equal("plugin", skill.Resolution);
    }

    // ── update_schedule ──────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateScheduleAsync_Success_ReturnsOk()
    {
        _jobService
            .Setup(s => s.UpdateAsync("job-1", It.IsAny<ScheduledJobInput>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ScheduledJobMutationResult.Succeeded(Job()));
        var tools = CreateTools();

        var json = await tools.UpdateScheduleAsync(id: "job-1", name: "Sample", prompt: "new prompt", cancellationToken: Ct);
        using var doc = JsonDocument.Parse(json);

        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("job-1", doc.RootElement.GetProperty("id").GetString());
    }

    [Fact]
    public async Task UpdateScheduleAsync_NotFound_ReturnsErrorString()
    {
        _jobService
            .Setup(s => s.UpdateAsync("missing", It.IsAny<ScheduledJobInput>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ScheduledJobMutationResult.Missing);
        var tools = CreateTools();

        var result = await tools.UpdateScheduleAsync(id: "missing", name: "Sample", prompt: "p", cancellationToken: Ct);

        Assert.StartsWith("Error:", result);
    }

    [Fact]
    public async Task UpdateScheduleAsync_Failure_ReturnsErrorString()
    {
        _jobService
            .Setup(s => s.UpdateAsync("job-1", It.IsAny<ScheduledJobInput>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ScheduledJobMutationResult.Failure("Task update failed: nope"));
        var tools = CreateTools();

        var result = await tools.UpdateScheduleAsync(id: "job-1", name: "Sample", prompt: "p", cancellationToken: Ct);

        Assert.Equal("Error: Task update failed: nope", result);
    }

    // ── set_schedule_enabled ─────────────────────────────────────────────────

    [Fact]
    public async Task SetScheduleEnabledAsync_Success_ReturnsOk()
    {
        _jobService
            .Setup(s => s.SetEnabledAsync("job-1", false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ScheduledJobMutationResult.Succeeded(Job()));
        var tools = CreateTools();

        var json = await tools.SetScheduleEnabledAsync("job-1", false, Ct);
        using var doc = JsonDocument.Parse(json);

        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public async Task SetScheduleEnabledAsync_NotFound_ReturnsErrorString()
    {
        _jobService
            .Setup(s => s.SetEnabledAsync("missing", true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ScheduledJobMutationResult.Missing);
        var tools = CreateTools();

        var result = await tools.SetScheduleEnabledAsync("missing", true, Ct);

        Assert.StartsWith("Error:", result);
    }

    // ── run_schedule_now ─────────────────────────────────────────────────────

    [Fact]
    public async Task RunScheduleNowAsync_Success_ReturnsOk()
    {
        _jobService.Setup(s => s.RunAsync("job-1", It.IsAny<CancellationToken>())).ReturnsAsync(ScheduledJobMutationResult.Succeeded(Job()));
        var tools = CreateTools();

        var json = await tools.RunScheduleNowAsync("job-1", Ct);
        using var doc = JsonDocument.Parse(json);

        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public async Task RunScheduleNowAsync_Failure_ReturnsErrorString()
    {
        _jobService
            .Setup(s => s.RunAsync("job-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ScheduledJobMutationResult.Failure("busy"));
        var tools = CreateTools();

        var result = await tools.RunScheduleNowAsync("job-1", Ct);

        Assert.Equal("Error: busy", result);
    }

    // ── delete_schedule ──────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteScheduleAsync_Success_ReturnsOk()
    {
        _jobService.Setup(s => s.DeleteAsync("job-1", It.IsAny<CancellationToken>())).ReturnsAsync(ScheduledJobMutationResult.Succeeded(Job()));
        var tools = CreateTools();

        var json = await tools.DeleteScheduleAsync("job-1", Ct);
        using var doc = JsonDocument.Parse(json);

        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("job-1", doc.RootElement.GetProperty("id").GetString());
    }

    [Fact]
    public async Task DeleteScheduleAsync_NotFound_ReturnsErrorString()
    {
        _jobService.Setup(s => s.DeleteAsync("missing", It.IsAny<CancellationToken>())).ReturnsAsync(ScheduledJobMutationResult.Missing);
        var tools = CreateTools();

        var result = await tools.DeleteScheduleAsync("missing", Ct);

        Assert.StartsWith("Error:", result);
    }

    [Fact]
    public async Task DeleteScheduleAsync_ServiceFailure_ReturnsErrorString()
    {
        _jobService.Setup(service => service.DeleteAsync(
                "job-1",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ScheduledJobMutationResult.Failure("task is locked"));

        var result = await CreateTools().DeleteScheduleAsync("job-1", Ct);

        Assert.Equal("Error: task is locked", result);
    }
}
