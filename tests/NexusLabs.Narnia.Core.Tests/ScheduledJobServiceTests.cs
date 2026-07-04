using NexusLabs.Narnia.Core.Models;
using NexusLabs.Narnia.Core.Repositories;
using NexusLabs.Narnia.Core.Services;

namespace NexusLabs.Narnia.Core.Tests;

public sealed class ScheduledJobServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 28, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly Mock<IScheduledJobRegistry> _registry = new();
    private readonly Mock<IScheduledTaskRegistrar> _registrar = new();
    private readonly Mock<IScheduledJobWorkspace> _workspace = new();
    private readonly Mock<IScheduledTaskProvider> _taskProvider = new();
    private readonly Mock<IPowerShellHostResolver> _hostResolver = new();

    public ScheduledJobServiceTests()
    {
        _registrar.SetupGet(r => r.IsSupported).Returns(true);
        _registrar
            .Setup(r => r.RegisterAsync(It.IsAny<ScheduledTaskRegistration>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ScheduledTaskCommandResult.Success);
        _registrar
            .Setup(r => r.SetEnabledAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ScheduledTaskCommandResult.Success);
        _registrar
            .Setup(r => r.RunAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ScheduledTaskCommandResult.Success);
        _registrar
            .Setup(r => r.DeleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ScheduledTaskCommandResult.Success);

        _workspace.Setup(w => w.ScriptPath(It.IsAny<string>())).Returns((string id) => $@"C:\narnia\{id}\run.ps1");
        _workspace.Setup(w => w.LauncherPath(It.IsAny<string>())).Returns((string id) => $@"C:\narnia\{id}\run.vbs");
        _workspace.Setup(w => w.LogDirectory(It.IsAny<string>())).Returns((string id) => $@"C:\narnia\{id}\logs");
        _workspace
            .Setup(w => w.WriteScriptAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string id, string _, CancellationToken _) => $@"C:\narnia\{id}\run.ps1");
        _workspace
            .Setup(w => w.WriteLauncherAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string id, string _, CancellationToken _) => $@"C:\narnia\{id}\run.vbs");

        _taskProvider.SetupGet(p => p.IsSupported).Returns(true);
        _taskProvider
            .Setup(p => p.ListInFolderAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ScheduledTaskStatus>)[]);

        _hostResolver.Setup(h => h.ResolveExecutable()).Returns("pwsh.exe");
    }

    private ScheduledJobService CreateService() =>
        new(_registry.Object, _registrar.Object, _workspace.Object, _taskProvider.Object, _hostResolver.Object);

    private static ScheduledJobInput Input(
        string name = "Sample",
        string? prompt = "do the thing",
        string? cadenceKind = "daily",
        string? time = "05:00",
        IReadOnlyList<string>? days = null,
        int? dayOfMonth = null) =>
        new(Name: name, Prompt: prompt, CadenceKind: cadenceKind, Time: time, Days: days, DayOfMonth: dayOfMonth);

    private static ScheduledJob Job(string id, string taskFolder = @"\Narnia\", string taskName = "Narnia - Sample") =>
        new(
            id, "Sample", null, @"C:\dev\x", "Daily 05:00", null, @"C:\narnia\run.ps1", @"C:\narnia\logs", "--allow-all-tools",
            taskFolder, taskName, null, Now, Now, [], Prompt: "do the thing", CadenceKind: "Daily", CadenceTime: "05:00");

    // ── Create ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_MissingName_ReturnsFailureWithoutTouchingCollaborators()
    {
        var service = CreateService();

        var result = await service.CreateAsync(Input(name: "  "), register: true, Ct);

        Assert.False(result.Ok);
        Assert.Contains("name", result.Error, StringComparison.OrdinalIgnoreCase);
        _registry.Verify(r => r.CreateWithIdAsync(It.IsAny<string>(), It.IsAny<ScheduledJobDraft>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_MissingPrompt_ReturnsFailure()
    {
        var service = CreateService();

        var result = await service.CreateAsync(Input(prompt: null), register: true, Ct);

        Assert.False(result.Ok);
        Assert.Contains("prompt", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateAsync_CopyPasteMode_ReturnsScriptAndCommand_CatalogsNothing()
    {
        var service = CreateService();

        var result = await service.CreateAsync(Input(), register: false, Ct);

        Assert.True(result.Ok);
        Assert.False(result.Registered);
        Assert.Contains("copilot -p", result.Script);
        Assert.Contains("Register-ScheduledTask", result.Command);
        _registry.Verify(r => r.CreateWithIdAsync(It.IsAny<string>(), It.IsAny<ScheduledJobDraft>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
        _workspace.Verify(w => w.WriteScriptAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _registrar.Verify(r => r.RegisterAsync(It.IsAny<ScheduledTaskRegistration>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_RegisterMode_RegistrarUnsupported_ReturnsFailure()
    {
        _registrar.SetupGet(r => r.IsSupported).Returns(false);
        var service = CreateService();

        var result = await service.CreateAsync(Input(), register: true, Ct);

        Assert.False(result.Ok);
        Assert.Contains("not supported", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateAsync_RegisterMode_Success_WritesScriptCatalogsAndRegisters()
    {
        _registry
            .Setup(r => r.CreateWithIdAsync(It.IsAny<string>(), It.IsAny<ScheduledJobDraft>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string id, ScheduledJobDraft draft, DateTimeOffset now, CancellationToken _) =>
                new ScheduledJob(id, draft.Name, draft.Description, draft.Cwd, draft.Cadence, draft.Args,
                    draft.ScriptPath, draft.LogDir, draft.AllowFlags, draft.TaskFolder, draft.TaskName, draft.Notes,
                    now, now, draft.Skills, draft.Prompt, draft.CadenceKind, draft.CadenceTime, draft.CadenceDays, draft.CopilotArgs));
        var service = CreateService();

        var result = await service.CreateAsync(Input(name: "Sample Daily", prompt: "run the thing"), register: true, Ct);

        Assert.True(result.Ok);
        Assert.True(result.Registered);
        Assert.Equal("Sample Daily", result.Job!.Name);
        Assert.Equal("run the thing", result.Job.Prompt);
        _workspace.Verify(w => w.WriteScriptAsync(result.Job.Id, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        _workspace.Verify(w => w.WriteLauncherAsync(result.Job.Id, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        _registrar.Verify(r => r.RegisterAsync(It.IsAny<ScheduledTaskRegistration>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_RegisterMode_RegistersViaHiddenWscriptLauncher_NeverABareVisibleConsole()
    {
        ScheduledTaskRegistration? captured = null;
        _registrar
            .Setup(r => r.RegisterAsync(It.IsAny<ScheduledTaskRegistration>(), It.IsAny<CancellationToken>()))
            .Callback<ScheduledTaskRegistration, CancellationToken>((reg, _) => captured = reg)
            .ReturnsAsync(ScheduledTaskCommandResult.Success);
        var service = CreateService();

        await service.CreateAsync(Input(name: "Sample"), register: true, Ct);

        Assert.NotNull(captured);
        Assert.Equal("wscript.exe", captured!.Execute);
        Assert.Contains(@"C:\narnia\", captured.Arguments);
        Assert.Contains("run.vbs", captured.Arguments);
    }

    [Fact]
    public async Task CreateAsync_CopyPasteMode_IncludesBothWrapperAndHiddenLauncherContent()
    {
        var service = CreateService();

        var result = await service.CreateAsync(Input(), register: false, Ct);

        Assert.True(result.Ok);
        Assert.Contains("copilot -p", result.Script);
        Assert.Contains("shell.Run(", result.Script);
        Assert.Contains("Register-ScheduledTask", result.Command);
    }

    [Fact]
    public async Task CreateAsync_UsesResolvedPowerShellHostInLauncher()
    {
        _hostResolver.Setup(h => h.ResolveExecutable()).Returns("powershell.exe");
        var service = CreateService();

        var result = await service.CreateAsync(Input(), register: false, Ct);

        Assert.Contains("powershell.exe -NoProfile", result.Script);
    }

    [Fact]
    public async Task CreateAsync_RegistrarFails_RollsBackCatalogAndWorkspace()
    {
        var createdId = string.Empty;
        _registry
            .Setup(r => r.CreateWithIdAsync(It.IsAny<string>(), It.IsAny<ScheduledJobDraft>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string id, ScheduledJobDraft draft, DateTimeOffset now, CancellationToken _) =>
            {
                createdId = id;
                return new ScheduledJob(id, draft.Name, draft.Description, draft.Cwd, draft.Cadence, draft.Args,
                    draft.ScriptPath, draft.LogDir, draft.AllowFlags, draft.TaskFolder, draft.TaskName, draft.Notes,
                    now, now, draft.Skills);
            });
        _registrar
            .Setup(r => r.RegisterAsync(It.IsAny<ScheduledTaskRegistration>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ScheduledTaskCommandResult.Fail("boom"));
        var service = CreateService();

        var result = await service.CreateAsync(Input(), register: true, Ct);

        Assert.False(result.Ok);
        Assert.Contains("boom", result.Error);
        _registry.Verify(r => r.DeleteAsync(createdId, It.IsAny<CancellationToken>()), Times.Once);
        _workspace.Verify(w => w.Delete(createdId), Times.Once);
    }

    // ── Update ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_UnknownId_ReturnsNotFound()
    {
        _registry.Setup(r => r.GetByIdAsync("missing", It.IsAny<CancellationToken>())).ReturnsAsync((ScheduledJob?)null);
        var service = CreateService();

        var result = await service.UpdateAsync("missing", Input(), Ct);

        Assert.True(result.NotFound);
        Assert.False(result.Ok);
    }

    [Fact]
    public async Task UpdateAsync_Success_RewritesScriptAndReRegisters()
    {
        var existing = Job("job-1");
        _registry.Setup(r => r.GetByIdAsync("job-1", It.IsAny<CancellationToken>())).ReturnsAsync(existing);
        var updated = existing with { Prompt = "new prompt" };
        var callCount = 0;
        _registry
            .Setup(r => r.GetByIdAsync("job-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => ++callCount == 1 ? existing : updated);
        var service = CreateService();

        var result = await service.UpdateAsync("job-1", Input(prompt: "new prompt"), Ct);

        Assert.True(result.Ok);
        Assert.Equal("new prompt", result.Job!.Prompt);
        _workspace.Verify(w => w.WriteScriptAsync("job-1", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        _workspace.Verify(w => w.WriteLauncherAsync("job-1", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        _registry.Verify(r => r.UpdateAsync("job-1", It.IsAny<ScheduledJobDraft>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Once);
        _registrar.Verify(r => r.RegisterAsync(It.IsAny<ScheduledTaskRegistration>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_RegistrarFails_ReturnsFailure()
    {
        _registry.Setup(r => r.GetByIdAsync("job-1", It.IsAny<CancellationToken>())).ReturnsAsync(Job("job-1"));
        _registrar
            .Setup(r => r.RegisterAsync(It.IsAny<ScheduledTaskRegistration>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ScheduledTaskCommandResult.Fail("nope"));
        var service = CreateService();

        var result = await service.UpdateAsync("job-1", Input(), Ct);

        Assert.False(result.Ok);
        Assert.False(result.NotFound);
        Assert.Contains("nope", result.Error);
    }

    // ── Enable / Run / Delete ────────────────────────────────────────────────

    [Fact]
    public async Task SetEnabledAsync_UnknownId_ReturnsNotFound()
    {
        var service = CreateService();

        var result = await service.SetEnabledAsync("missing", true, Ct);

        Assert.True(result.NotFound);
    }

    [Fact]
    public async Task SetEnabledAsync_Success_CallsRegistrarWithJobsTaskIdentity()
    {
        var job = Job("job-1", taskFolder: @"\Narnia\", taskName: "Narnia - Sample");
        _registry.Setup(r => r.GetByIdAsync("job-1", It.IsAny<CancellationToken>())).ReturnsAsync(job);
        var service = CreateService();

        var result = await service.SetEnabledAsync("job-1", false, Ct);

        Assert.True(result.Ok);
        _registrar.Verify(r => r.SetEnabledAsync(@"\Narnia\", "Narnia - Sample", false, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunAsync_UnknownId_ReturnsNotFound()
    {
        var service = CreateService();

        var result = await service.RunAsync("missing", Ct);

        Assert.True(result.NotFound);
    }

    [Fact]
    public async Task RunAsync_RegistrarFails_ReturnsFailure()
    {
        _registry.Setup(r => r.GetByIdAsync("job-1", It.IsAny<CancellationToken>())).ReturnsAsync(Job("job-1"));
        _registrar
            .Setup(r => r.RunAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ScheduledTaskCommandResult.Fail("busy"));
        var service = CreateService();

        var result = await service.RunAsync("job-1", Ct);

        Assert.False(result.Ok);
        Assert.Contains("busy", result.Error);
    }

    [Fact]
    public async Task DeleteAsync_UnknownId_ReturnsNotFound()
    {
        var service = CreateService();

        var result = await service.DeleteAsync("missing", Ct);

        Assert.True(result.NotFound);
        _registrar.Verify(r => r.DeleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteAsync_Success_RemovesTaskWorkspaceAndCatalogEntry()
    {
        var job = Job("job-1", taskFolder: @"\Narnia\", taskName: "Narnia - Sample");
        _registry.Setup(r => r.GetByIdAsync("job-1", It.IsAny<CancellationToken>())).ReturnsAsync(job);
        var service = CreateService();

        var result = await service.DeleteAsync("job-1", Ct);

        Assert.True(result.Ok);
        _registrar.Verify(r => r.DeleteAsync(@"\Narnia\", "Narnia - Sample", It.IsAny<CancellationToken>()), Times.Once);
        _workspace.Verify(w => w.Delete("job-1"), Times.Once);
        _registry.Verify(r => r.DeleteAsync("job-1", It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── List / Get ───────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAsync_DelegatesToRegistry()
    {
        var job = Job("job-1");
        _registry.Setup(r => r.GetByIdAsync("job-1", It.IsAny<CancellationToken>())).ReturnsAsync(job);
        var service = CreateService();

        var result = await service.GetAsync("job-1", Ct);

        Assert.Same(job, result);
    }

    [Fact]
    public async Task ListAsync_JoinsCatalogToLiveStatus_ByFolderAndName()
    {
        var job = Job("job-1", taskFolder: @"\Narnia\", taskName: "Narnia - Sample");
        _registry.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync((IReadOnlyList<ScheduledJob>)[job]);
        var status = new ScheduledTaskStatus(@"\Narnia\", "Narnia - Sample", ScheduledTaskState.Ready, null, 0, Now.AddDays(1), "powershell.exe");
        _taskProvider
            .Setup(p => p.ListInFolderAsync(@"\Narnia\", It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ScheduledTaskStatus>)[status]);
        var service = CreateService();

        var view = await service.ListAsync(Ct);

        var jobView = Assert.Single(view.Jobs);
        Assert.True(jobView.TaskFound);
        Assert.Same(status, jobView.Status);
        Assert.Empty(view.Untracked);
    }

    [Fact]
    public async Task ListAsync_JobOutsideNarniaFolder_ResolvedViaDirectGet()
    {
        var job = Job("job-1", taskFolder: @"\External\", taskName: "External - Sample");
        _registry.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync((IReadOnlyList<ScheduledJob>)[job]);
        var status = new ScheduledTaskStatus(@"\External\", "External - Sample", ScheduledTaskState.Ready, null, 0, null, null);
        _taskProvider
            .Setup(p => p.GetAsync(@"\External\", "External - Sample", It.IsAny<CancellationToken>()))
            .ReturnsAsync(status);
        var service = CreateService();

        var view = await service.ListAsync(Ct);

        var jobView = Assert.Single(view.Jobs);
        Assert.True(jobView.TaskFound);
        _taskProvider.Verify(p => p.GetAsync(@"\External\", "External - Sample", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ListAsync_UnmatchedTaskInNarniaFolder_SurfacedAsUntracked()
    {
        _registry.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync((IReadOnlyList<ScheduledJob>)[]);
        var status = new ScheduledTaskStatus(@"\Narnia\", "Hand-made", ScheduledTaskState.Ready, null, null, null, null);
        _taskProvider
            .Setup(p => p.ListInFolderAsync(@"\Narnia\", It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ScheduledTaskStatus>)[status]);
        var service = CreateService();

        var view = await service.ListAsync(Ct);

        Assert.Empty(view.Jobs);
        var untracked = Assert.Single(view.Untracked);
        Assert.Equal("Hand-made", untracked.TaskName);
    }

    [Fact]
    public async Task ListAsync_ReportsSchedulerUnsupported()
    {
        _taskProvider.SetupGet(p => p.IsSupported).Returns(false);
        _registry.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync((IReadOnlyList<ScheduledJob>)[]);
        var service = CreateService();

        var view = await service.ListAsync(Ct);

        Assert.False(view.SchedulerSupported);
    }

    [Fact]
    public void RegistrarSupported_ReflectsRegistrar()
    {
        _registrar.SetupGet(r => r.IsSupported).Returns(false);
        var service = CreateService();

        Assert.False(service.RegistrarSupported);
    }

    // ── Get latest log ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetLatestLogAsync_UnknownJob_ReturnsJobNotFound()
    {
        var service = CreateService();

        var log = await service.GetLatestLogAsync("missing", Ct);

        Assert.True(log.JobNotFound);
        Assert.False(log.Found);
    }

    [Fact]
    public async Task GetLatestLogAsync_JobNeverRun_ReturnsNoLogYet()
    {
        _registry.Setup(r => r.GetByIdAsync("job-1", It.IsAny<CancellationToken>())).ReturnsAsync(Job("job-1"));
        _workspace.Setup(w => w.LatestLogFile("job-1")).Returns((string?)null);
        var service = CreateService();

        var log = await service.GetLatestLogAsync("job-1", Ct);

        Assert.False(log.JobNotFound);
        Assert.False(log.Found);
    }

    [Fact]
    public async Task GetLatestLogAsync_LogExists_ReturnsPathAndContent()
    {
        _registry.Setup(r => r.GetByIdAsync("job-1", It.IsAny<CancellationToken>())).ReturnsAsync(Job("job-1"));
        _workspace.Setup(w => w.LatestLogFile("job-1")).Returns(@"C:\narnia\job-1\logs\run-2026-07-04_020000.log");
        _workspace
            .Setup(w => w.ReadLogAsync(@"C:\narnia\job-1\logs\run-2026-07-04_020000.log", It.IsAny<CancellationToken>()))
            .ReturnsAsync("=== Sample ===\nExitCode: 1");
        var service = CreateService();

        var log = await service.GetLatestLogAsync("job-1", Ct);

        Assert.True(log.Found);
        Assert.Equal(@"C:\narnia\job-1\logs\run-2026-07-04_020000.log", log.Path);
        Assert.Contains("ExitCode: 1", log.Content);
        Assert.False(log.Truncated);
    }

    [Fact]
    public async Task GetLatestLogAsync_LargeLog_TruncatesToTailAndFlagsTruncated()
    {
        _registry.Setup(r => r.GetByIdAsync("job-1", It.IsAny<CancellationToken>())).ReturnsAsync(Job("job-1"));
        _workspace.Setup(w => w.LatestLogFile("job-1")).Returns(@"C:\narnia\job-1\logs\run-x.log");
        var hugeContent = new string('a', 150_000) + "TAIL-MARKER";
        _workspace
            .Setup(w => w.ReadLogAsync(@"C:\narnia\job-1\logs\run-x.log", It.IsAny<CancellationToken>()))
            .ReturnsAsync(hugeContent);
        var service = CreateService();

        var log = await service.GetLatestLogAsync("job-1", Ct);

        Assert.True(log.Truncated);
        Assert.EndsWith("TAIL-MARKER", log.Content);
        Assert.True(log.Content!.Length < hugeContent.Length);
    }
}
