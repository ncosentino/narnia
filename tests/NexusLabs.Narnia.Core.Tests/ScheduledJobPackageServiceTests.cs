using System.IO.Abstractions.TestingHelpers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NexusLabs.Narnia.Core.Configuration;
using NexusLabs.Narnia.Core.Models;
using NexusLabs.Narnia.Core.Repositories;
using NexusLabs.Narnia.Core.Services;

namespace NexusLabs.Narnia.Core.Tests;

public sealed class ScheduledJobPackageServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 25, 12, 0, 0, TimeSpan.Zero);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly Mock<IScheduledJobService> _jobs = new();
    private readonly Mock<IScheduledJobImportRepository> _imports = new();
    private readonly MockFileSystem _fileSystem = new();
    private readonly NarniaOptions _options = new()
    {
        InstalledPluginsPath = @"C:\copilot\installed-plugins",
    };

    public ScheduledJobPackageServiceTests()
    {
        _jobs.SetupGet(service => service.RegistrarSupported).Returns(true);
        _jobs.Setup(service => service.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScheduledJobListView(true, [], []));
        _imports.Setup(repository => repository.GetActiveAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ScheduledJobImportRecord>)[]);
    }

    private ScheduledJobPackageService CreateService() =>
        new(_jobs.Object, _imports.Object, _options, _fileSystem);

    private static ScheduledJobDefinition Definition(
        string name,
        string prompt,
        string? cwd) =>
        Definition(name, prompt, cwd, []);

    private static ScheduledJobDefinition Definition(
        string name,
        string prompt,
        string? cwd,
        IReadOnlyList<ScheduledJobSkill> skills) =>
        new(
            name,
            $"{name} description",
            cwd,
            prompt,
            "--allow-all-tools --allow-all-paths",
            null,
            $"Narnia - {name}",
            new ScheduleCadence(
                ScheduleCadenceKind.Weekly,
                new TimeOnly(6, 30),
                [DayOfWeek.Monday, DayOfWeek.Friday]),
            skills);

    private static ScheduledJob StoredJob(
        string id,
        ScheduledJobDefinition definition) =>
        new(
            id,
            definition.Name,
            definition.Description,
            definition.WorkingDirectory,
            definition.Cadence.Describe(),
            null,
            $@"C:\narnia\schedules\{id}\run.ps1",
            $@"C:\narnia\schedules\{id}\logs",
            definition.AllowFlags,
            @"\Narnia\",
            definition.TaskName,
            null,
            Now,
            Now,
            definition.Skills,
            definition.Prompt,
            definition.Cadence.Kind.ToString(),
            definition.Cadence.TimeOfDay.ToString("HH\\:mm"),
            string.Join(",", definition.Cadence.DaysOfWeek),
            definition.CopilotArgs);

    private static ScheduledJobStatusView View(ScheduledJob job) =>
        View(job, ScheduledTaskState.Ready);

    private static ScheduledJobStatusView View(
        ScheduledJob job,
        ScheduledTaskState state) =>
        new(
            job,
            new ScheduledTaskStatus(
                job.TaskFolder,
                job.TaskName,
                state,
                null,
                null,
                Now.AddDays(1),
                "wscript.exe run.vbs"),
            true);

    [Fact]
    public async Task ExportAsync_TransfersDefinitionAndReplacesMachinePathsWithBindings()
    {
        _fileSystem.AddDirectory(@"C:\src\sample");
        _fileSystem.AddFile(@"C:\config\sample.json", new MockFileData("{}"));
        _fileSystem.AddFile(
            @"C:\copilot\installed-plugins\market\plugin\skills\sample-skill\SKILL.md",
            new MockFileData("# Sample"));
        _fileSystem.AddFile(
            @"C:\copilot\installed-plugins\market\plugin\.claude-plugin\marketplace.json",
            new MockFileData("""{"metadata":{"version":"1.2.3"}}"""));
        var definition = Definition(
            "Sample",
            @"Run sample-skill using ""C:\config\sample.json"" from C:\src\sample.",
            @"C:\src\sample",
            [new ScheduledJobSkill("sample-skill", SkillResolution.Plugin, 0)]);
        var job = StoredJob("job-1", definition);
        _jobs.Setup(service => service.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScheduledJobListView(true, [View(job)], []));

        var result = await CreateService().ExportAsync(
            new ScheduledJobPackageExportRequest(
                [job.Id],
                ScheduledJobPackageProfile.Transfer),
            Ct);

        Assert.True(result.Ok);
        Assert.NotNull(result.Package);
        var package = result.Package!;
        var packagedJob = Assert.Single(package.Jobs);
        Assert.Equal(job.Id, packagedJob.PortableJobId);
        Assert.Equal(job.Id, packagedJob.SourceJobId);
        Assert.True(packagedJob.SourceEnabled);
        Assert.DoesNotContain(@"C:\src\sample", packagedJob.Definition.PromptTemplate, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(@"C:\config\sample.json", packagedJob.Definition.PromptTemplate, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("{{narnia:", packagedJob.Definition.PromptTemplate, StringComparison.Ordinal);
        Assert.Equal(2, package.Bindings.Count);
        Assert.All(package.Bindings, binding => Assert.NotNull(binding.SourceHint));
        var dependency = Assert.Single(package.Dependencies);
        Assert.Equal("plugin", dependency.PluginName);
        Assert.Equal("market", dependency.Marketplace);
        Assert.Equal("1.2.3", dependency.Version);
        Assert.DoesNotContain("run.ps1", result.PackageJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("logDir", result.PackageJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExportAsync_ShareProfile_RemovesSourceLocalHintsAndIdentity()
    {
        _fileSystem.AddDirectory(@"C:\src\sample");
        var job = StoredJob(
            "job-1",
            Definition("Sample", "Do the thing", @"C:\src\sample"));
        _jobs.Setup(service => service.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScheduledJobListView(true, [View(job)], []));

        var result = await CreateService().ExportAsync(
            new ScheduledJobPackageExportRequest(
                [job.Id],
                ScheduledJobPackageProfile.Share),
            Ct);

        var packagedJob = Assert.Single(result.Package!.Jobs);
        Assert.NotEqual(job.Id, packagedJob.PortableJobId);
        Assert.Null(packagedJob.SourceJobId);
        Assert.Null(packagedJob.SourceTaskName);
        Assert.Null(packagedJob.SourceEnabled);
        Assert.All(result.Package.Bindings, binding => Assert.Null(binding.SourceHint));
        Assert.DoesNotContain(@"C:\src\sample", result.PackageJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BuildAsync_WorkingDirectoryPrefix_DoesNotCorruptUnrelatedPath()
    {
        _fileSystem.AddDirectory(@"C:\Data");
        _fileSystem.AddFile(@"C:\Database\backup.log", new MockFileData("log"));

        var result = await CreateService().BuildAsync(
            new ScheduledJobPackageBuildRequest(
                [
                    Definition(
                        "Prefix",
                        @"Summarize C:\Data and inspect C:\Database\backup.log.",
                        @"C:\Data"),
                ],
                [],
                ScheduledJobPackageProfile.Transfer),
            Ct);

        var prompt = Assert.Single(result.Package!.Jobs).Definition.PromptTemplate;
        Assert.Contains("{{narnia:data}}", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("{{narnia:data}}base", prompt, StringComparison.Ordinal);
        Assert.Contains(result.Package.Bindings, binding => binding.SourceHint == @"C:\Database\backup.log");
    }

    [Fact]
    public async Task BuildAsync_CopilotArgumentPath_IsBoundAndResolved()
    {
        _fileSystem.AddFile(@"C:\configs\agent.json", new MockFileData("{}"));
        _fileSystem.AddFile(@"D:\target\agent.json", new MockFileData("{}"));
        var definition = Definition("Arguments", "Run", null) with
        {
            CopilotArgs = @"--config C:\configs\agent.json",
        };
        var built = await CreateService().BuildAsync(
            new ScheduledJobPackageBuildRequest(
                [definition],
                [],
                ScheduledJobPackageProfile.Share),
            Ct);
        var binding = Assert.Single(built.Package!.Bindings);
        Assert.DoesNotContain(@"C:\configs\agent.json", built.PackageJson, StringComparison.OrdinalIgnoreCase);

        var preview = await CreateService().PreviewAsync(
            new ScheduledJobPackagePreviewRequest(
                built.PackageJson!,
                [new ScheduledJobPackageBindingValue(binding.Id, @"D:\target\agent.json")],
                []),
            Ct);

        var rendered = Assert.Single(preview.Jobs).RenderedDefinition;
        Assert.Equal(@"--config D:\target\agent.json", rendered!.CopilotArgs);
    }

    [Fact]
    public async Task BuildAsync_ExternalStateRequirement_IsPreservedAsPreviewWarning()
    {
        var dependency = new ScheduledJobPackageDependency(
            "state-cache",
            ScheduledJobPackageDependencyKind.ExternalState,
            "Durable radar state",
            true,
            null,
            null,
            null,
            null,
            null,
            "Move the durable radar state separately before enabling this job.");
        var built = await CreateService().BuildAsync(
            new ScheduledJobPackageBuildRequest(
                [Definition("Stateful", "Run", null)],
                [dependency],
                ScheduledJobPackageProfile.Transfer),
            Ct);

        Assert.Contains(built.Package!.Dependencies, item => item.Id == "state-cache");
        var preview = await CreateService().PreviewAsync(
            new ScheduledJobPackagePreviewRequest(built.PackageJson!, [], []),
            Ct);

        var job = Assert.Single(preview.Jobs);
        Assert.True(job.CanImport);
        Assert.Contains(job.Findings, finding => finding.Code == "external-state-omitted");
    }

    [Fact]
    public async Task BuildAsync_CredentialLikeLiteral_IsRejectedWithoutProducingPackage()
    {
        var result = await CreateService().BuildAsync(
            new ScheduledJobPackageBuildRequest(
                [Definition("Unsafe", "Run with api_key=abcdefgh12345678", null)],
                [],
                ScheduledJobPackageProfile.Transfer),
            Ct);

        Assert.False(result.Ok);
        Assert.Null(result.Package);
        Assert.Null(result.PackageJson);
        Assert.Contains("credential", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PreviewAsync_BoundDirectoryPrefix_DoesNotHideSiblingAbsolutePath()
    {
        _fileSystem.AddDirectory(@"C:\work");
        var portableDefinition = new ScheduledJobPortableDefinition(
            "Sibling",
            null,
            @"Use {{narnia:work}} and inspect C:\worksecrets\passwords.txt.",
            "work",
            null,
            null,
            "Sibling",
            new ScheduledJobPackageCadence(
                ScheduleCadenceKind.Daily,
                "05:00",
                [],
                1),
            []);
        var package = new ScheduledJobPackage(
            ScheduledJobPackageService.PackageFormat,
            ScheduledJobPackageService.CurrentSchemaVersion,
            "package-sibling",
            ScheduledJobPackageProfile.Share,
            Now,
            new ScheduledJobPackageSource("test", TimeZoneInfo.Local.Id),
            [
                new ScheduledJobPackageBinding(
                    "work",
                    ScheduledJobPackageBindingKind.Directory,
                    "Work root",
                    true,
                    null,
                    null,
                    null),
            ],
            [],
            [
                new ScheduledJobPackageJob(
                    "portable-sibling",
                    Fingerprint(portableDefinition),
                    portableDefinition,
                    null,
                    null,
                    null),
            ]);

        var preview = await CreateService().PreviewAsync(
            new ScheduledJobPackagePreviewRequest(
                Serialize(package),
                [new ScheduledJobPackageBindingValue("work", @"C:\work")],
                []),
            Ct);

        var job = Assert.Single(preview.Jobs);
        Assert.Contains(job.Findings, finding => finding.Code == "unbound-absolute-path");
    }

    [Fact]
    public async Task PreviewAsync_MissingRequiredBinding_IsNotImportable()
    {
        _fileSystem.AddDirectory(@"C:\source");
        var built = await CreateService().BuildAsync(
            new ScheduledJobPackageBuildRequest(
                [Definition("Bound", "Run here", @"C:\source")],
                [],
                ScheduledJobPackageProfile.Share),
            Ct);

        var preview = await CreateService().PreviewAsync(
            new ScheduledJobPackagePreviewRequest(built.PackageJson!, [], []),
            Ct);

        Assert.True(preview.Ok);
        var job = Assert.Single(preview.Jobs);
        Assert.Equal(ScheduledJobPackagePreviewStatus.NeedsBinding, job.Status);
        Assert.False(job.CanImport);
        Assert.Contains(job.Findings, finding => finding.Code == "binding-required");
    }

    [Fact]
    public async Task PreviewAsync_TaskNameConflict_IsNotImportable()
    {
        var definition = Definition("Conflicting", "Run", null);
        var built = await CreateService().BuildAsync(
            new ScheduledJobPackageBuildRequest(
                [definition],
                [],
                ScheduledJobPackageProfile.Transfer),
            Ct);
        var existing = StoredJob("existing", definition);
        _jobs.Setup(service => service.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScheduledJobListView(true, [View(existing)], []));

        var preview = await CreateService().PreviewAsync(
            new ScheduledJobPackagePreviewRequest(built.PackageJson!, [], []),
            Ct);

        var job = Assert.Single(preview.Jobs);
        Assert.Equal(ScheduledJobPackagePreviewStatus.TaskNameConflict, job.Status);
        Assert.False(job.CanImport);
    }

    [Fact]
    public async Task PreviewAsync_TaskNameOverride_ResolvesConflict()
    {
        var definition = Definition("Conflicting", "Run", null);
        var built = await CreateService().BuildAsync(
            new ScheduledJobPackageBuildRequest(
                [definition],
                [],
                ScheduledJobPackageProfile.Transfer),
            Ct);
        var existing = StoredJob("existing", definition);
        _jobs.Setup(service => service.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScheduledJobListView(true, [View(existing)], []));
        var portableId = Assert.Single(built.Package!.Jobs).PortableJobId;

        var preview = await CreateService().PreviewAsync(
            new ScheduledJobPackagePreviewRequest(
                built.PackageJson!,
                [],
                [new ScheduledJobPackageJobOptions(portableId, "Narnia - Imported Copy", false, false)]),
            Ct);

        var job = Assert.Single(preview.Jobs);
        Assert.True(job.CanImport);
        Assert.Equal("Narnia - Imported Copy", job.TargetTaskName);
    }

    [Fact]
    public async Task ImportAsync_CreatesDisabledJobAndStoresProvenance()
    {
        var definition = Definition("Import Me", "Run", null);
        var built = await CreateService().BuildAsync(
            new ScheduledJobPackageBuildRequest(
                [definition],
                [],
                ScheduledJobPackageProfile.Transfer),
            Ct);
        var preview = await CreateService().PreviewAsync(
            new ScheduledJobPackagePreviewRequest(built.PackageJson!, [], []),
            Ct);
        ScheduledJobInput? capturedInput = null;
        _jobs.Setup(service => service.CreateDisabledAsync(
                It.IsAny<ScheduledJobInput>(),
                It.IsAny<CancellationToken>()))
            .Callback<ScheduledJobInput, CancellationToken>((input, _) => capturedInput = input)
            .ReturnsAsync((ScheduledJobInput input, CancellationToken _) =>
            {
                var mapped = new ScheduledJobDefinition(
                    input.Name,
                    input.Description,
                    input.Cwd,
                    input.Prompt!,
                    input.AllowFlags,
                    input.CopilotArgs,
                    input.TaskName!,
                    new ScheduleCadence(
                        ScheduleCadenceKind.Weekly,
                        new TimeOnly(6, 30),
                        [DayOfWeek.Monday, DayOfWeek.Friday]),
                    []);
                return ScheduledJobCreateResult.Created(StoredJob("local-1", mapped));
            });

        var result = await CreateService().ImportAsync(
            new ScheduledJobPackageImportRequest(
                built.PackageJson!,
                [],
                [],
                preview.PreviewFingerprint!),
            Ct);

        Assert.True(result.Ok);
        Assert.NotNull(capturedInput);
        Assert.Equal("Import Me", capturedInput!.Name);
        var imported = Assert.Single(result.Jobs);
        Assert.Equal("local-1", imported.LocalJobId);
        Assert.NotNull(result.Receipt);
        _imports.Verify(repository => repository.AddAsync(
            It.Is<ScheduledJobImportRecord>(record =>
                record.JobId == "local-1" &&
                record.PackageId == built.Package!.PackageId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ImportAsync_StalePreview_IsRejectedWithoutCreatingJobs()
    {
        var built = await CreateService().BuildAsync(
            new ScheduledJobPackageBuildRequest(
                [Definition("Import Me", "Run", null)],
                [],
                ScheduledJobPackageProfile.Transfer),
            Ct);

        var result = await CreateService().ImportAsync(
            new ScheduledJobPackageImportRequest(
                built.PackageJson!,
                [],
                [],
                "stale"),
            Ct);

        Assert.False(result.Ok);
        Assert.Contains("stale", result.Error, StringComparison.OrdinalIgnoreCase);
        _jobs.Verify(service => service.CreateDisabledAsync(
            It.IsAny<ScheduledJobInput>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ImportAsync_SecondCreateFailure_RollsBackFirstJob()
    {
        var built = await CreateService().BuildAsync(
            new ScheduledJobPackageBuildRequest(
                [
                    Definition("First", "Run first", null),
                    Definition("Second", "Run second", null),
                ],
                [],
                ScheduledJobPackageProfile.Transfer),
            Ct);
        var preview = await CreateService().PreviewAsync(
            new ScheduledJobPackagePreviewRequest(built.PackageJson!, [], []),
            Ct);
        var call = 0;
        _jobs.Setup(service => service.CreateDisabledAsync(
                It.IsAny<ScheduledJobInput>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((ScheduledJobInput input, CancellationToken _) =>
            {
                call++;
                if (call == 2)
                    return ScheduledJobCreateResult.Failure("second failed");
                var mapped = Definition(input.Name, input.Prompt!, null);
                return ScheduledJobCreateResult.Created(StoredJob("local-first", mapped));
            });
        _jobs.Setup(service => service.DeleteAsync(
                "local-first",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ScheduledJobMutationResult.Succeeded());

        var result = await CreateService().ImportAsync(
            new ScheduledJobPackageImportRequest(
                built.PackageJson!,
                [],
                [],
                preview.PreviewFingerprint!),
            Ct);

        Assert.False(result.Ok);
        Assert.Contains("rolled back", result.Error, StringComparison.OrdinalIgnoreCase);
        _jobs.Verify(service => service.DeleteAsync(
            "local-first",
            It.IsAny<CancellationToken>()), Times.Once);
        _imports.Verify(repository => repository.DeleteAsync(
            "local-first",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ImportAsync_RollbackTaskDeletionFailure_PreservesProvenance()
    {
        var built = await CreateService().BuildAsync(
            new ScheduledJobPackageBuildRequest(
                [
                    Definition("First", "Run first", null),
                    Definition("Second", "Run second", null),
                ],
                [],
                ScheduledJobPackageProfile.Transfer),
            Ct);
        var preview = await CreateService().PreviewAsync(
            new ScheduledJobPackagePreviewRequest(built.PackageJson!, [], []),
            Ct);
        var call = 0;
        _jobs.Setup(service => service.CreateDisabledAsync(
                It.IsAny<ScheduledJobInput>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((ScheduledJobInput input, CancellationToken _) =>
            {
                call++;
                return call == 1
                    ? ScheduledJobCreateResult.Created(
                        StoredJob("local-first", Definition(input.Name, input.Prompt!, null)))
                    : ScheduledJobCreateResult.Failure("second failed");
            });
        _jobs.Setup(service => service.DeleteAsync(
                "local-first",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ScheduledJobMutationResult.Failure("task is locked"));

        var result = await CreateService().ImportAsync(
            new ScheduledJobPackageImportRequest(
                built.PackageJson!,
                [],
                [],
                preview.PreviewFingerprint!),
            Ct);

        Assert.False(result.Ok);
        Assert.Contains("cleanup is required", result.Error, StringComparison.OrdinalIgnoreCase);
        _imports.Verify(repository => repository.DeleteAsync(
            "local-first",
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ImportAsync_SkippedJob_DoesNotBlockOrMaterializeRemainingBundle()
    {
        var built = await CreateService().BuildAsync(
            new ScheduledJobPackageBuildRequest(
                [
                    Definition("Needs Mapping", "Run mapped", @"C:\missing"),
                    Definition("Ready", "Run ready", null),
                ],
                [],
                ScheduledJobPackageProfile.Share),
            Ct);
        var skippedId = built.Package!.Jobs[0].PortableJobId;
        var options = new[]
        {
            new ScheduledJobPackageJobOptions(skippedId, null, false, true),
        };
        var preview = await CreateService().PreviewAsync(
            new ScheduledJobPackagePreviewRequest(
                built.PackageJson!,
                [],
                options),
            Ct);
        var skipped = preview.Jobs.Single(job => job.PortableJobId == skippedId);
        Assert.True(skipped.CanImport);
        Assert.False(skipped.WillImport);

        _jobs.Setup(service => service.CreateDisabledAsync(
                It.IsAny<ScheduledJobInput>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((ScheduledJobInput input, CancellationToken _) =>
                ScheduledJobCreateResult.Created(
                    StoredJob("local-ready", Definition(input.Name, input.Prompt!, null))));

        var result = await CreateService().ImportAsync(
            new ScheduledJobPackageImportRequest(
                built.PackageJson!,
                [],
                options,
                preview.PreviewFingerprint!),
            Ct);

        Assert.True(result.Ok);
        Assert.Single(result.Jobs);
        Assert.Equal("Narnia - Ready", Assert.Single(result.Receipt!.Jobs).TaskName);
        _jobs.Verify(service => service.CreateDisabledAsync(
            It.IsAny<ScheduledJobInput>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PreviewAsync_ModifiedDefinitionFingerprint_IsRejected()
    {
        var built = await CreateService().BuildAsync(
            new ScheduledJobPackageBuildRequest(
                [Definition("Original", "Run", null)],
                [],
                ScheduledJobPackageProfile.Transfer),
            Ct);
        var modified = built.PackageJson!.Replace(
            "\"name\": \"Original\"",
            "\"name\": \"Changed\"",
            StringComparison.Ordinal);

        var preview = await CreateService().PreviewAsync(
            new ScheduledJobPackagePreviewRequest(modified, [], []),
            Ct);

        Assert.False(preview.Ok);
        Assert.Contains("fingerprint", preview.Error, StringComparison.OrdinalIgnoreCase);
    }

    private static string Fingerprint(ScheduledJobPortableDefinition definition) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(definition, PackageJsonOptions()))));

    private static string Serialize(ScheduledJobPackage package) =>
        JsonSerializer.Serialize(package, PackageJsonOptions());

    private static JsonSerializerOptions PackageJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
