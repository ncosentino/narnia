using System.Text.Json;
using NexusLabs.Narnia.Core.Models;
using NexusLabs.Narnia.Core.Services;
using NexusLabs.Narnia.Web.Mcp;

namespace NexusLabs.Narnia.Web.Tests;

public sealed class SchedulePackageToolsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly Mock<IScheduledJobPackageService> _packages = new();

    private SchedulePackageTools CreateTools() => new(_packages.Object);

    [Fact]
    public async Task ExportSchedulePackageAsync_PassesIdsAndProfile()
    {
        ScheduledJobPackageExportRequest? captured = null;
        _packages.Setup(service => service.ExportAsync(
                It.IsAny<ScheduledJobPackageExportRequest>(),
                It.IsAny<CancellationToken>()))
            .Callback<ScheduledJobPackageExportRequest, CancellationToken>((request, _) => captured = request)
            .ReturnsAsync(new ScheduledJobPackageExportResult(
                true,
                null,
                null,
                "{}",
                []));

        var json = await CreateTools().ExportSchedulePackageAsync(
            ["one", "two"],
            "share",
            Ct);

        Assert.NotNull(captured);
        Assert.Equal(["one", "two"], captured!.JobIds);
        Assert.Equal(ScheduledJobPackageProfile.Share, captured.Profile);
        using var document = JsonDocument.Parse(json);
        Assert.True(document.RootElement.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public async Task BuildSchedulePackageAsync_NormalizesDefinitions()
    {
        ScheduledJobPackageBuildRequest? captured = null;
        _packages.Setup(service => service.BuildAsync(
                It.IsAny<ScheduledJobPackageBuildRequest>(),
                It.IsAny<CancellationToken>()))
            .Callback<ScheduledJobPackageBuildRequest, CancellationToken>((request, _) => captured = request)
            .ReturnsAsync(new ScheduledJobPackageExportResult(
                true,
                null,
                null,
                "{}",
                []));
        var input = new SchedulePackageJobMcpInput(
            "External",
            "Run a skill",
            @"C:\repo",
            "desc",
            "weekly",
            "06:30",
            ["Friday"],
            null,
            "--allow-all-tools",
            null,
            "External Task",
            [new ScheduleSkillMcpInput("sample", "repolocal")]);

        await CreateTools().BuildSchedulePackageAsync(
            [input],
            [
                new SchedulePackageDependencyMcpInput(
                    "state",
                    "externalState",
                    "Durable state",
                    true,
                    null,
                    null,
                    "Move separately"),
            ],
            "transfer",
            Ct);

        var definition = Assert.Single(captured!.Definitions);
        Assert.Equal(ScheduleCadenceKind.Weekly, definition.Cadence.Kind);
        Assert.Equal([DayOfWeek.Friday], definition.Cadence.DaysOfWeek);
        Assert.Equal(SkillResolution.RepoLocal, Assert.Single(definition.Skills).Resolution);
        Assert.Equal(
            ScheduledJobPackageDependencyKind.ExternalState,
            Assert.Single(captured.AdditionalDependencies).Kind);
    }

    [Fact]
    public async Task PreviewSchedulePackageAsync_PassesBindingsAndJobOptions()
    {
        ScheduledJobPackagePreviewRequest? captured = null;
        _packages.Setup(service => service.PreviewAsync(
                It.IsAny<ScheduledJobPackagePreviewRequest>(),
                It.IsAny<CancellationToken>()))
            .Callback<ScheduledJobPackagePreviewRequest, CancellationToken>((request, _) => captured = request)
            .ReturnsAsync(new ScheduledJobPackagePreviewResult(
                true,
                null,
                "package",
                "package-fingerprint",
                "preview-fingerprint",
                [],
                []));

        await CreateTools().PreviewSchedulePackageAsync(
            "{}",
            [new SchedulePackageBindingMcpInput("repo", @"D:\repo")],
            [new SchedulePackageJobOptionsMcpInput("portable", "Imported", true, false)],
            Ct);

        Assert.Equal(@"D:\repo", Assert.Single(captured!.Bindings).Value);
        var options = Assert.Single(captured.JobOptions);
        Assert.Equal("Imported", options.TaskName);
        Assert.True(options.AllowDuplicate);
        Assert.False(options.Skip);
    }

    [Fact]
    public async Task ImportSchedulePackageAsync_PassesAcceptedFingerprint()
    {
        ScheduledJobPackageImportRequest? captured = null;
        _packages.Setup(service => service.ImportAsync(
                It.IsAny<ScheduledJobPackageImportRequest>(),
                It.IsAny<CancellationToken>()))
            .Callback<ScheduledJobPackageImportRequest, CancellationToken>((request, _) => captured = request)
            .ReturnsAsync(new ScheduledJobPackageImportResult(true, null, [], null));

        await CreateTools().ImportSchedulePackageAsync(
            "{}",
            "accepted",
            [],
            [],
            Ct);

        Assert.Equal("accepted", captured!.PreviewFingerprint);
    }

    [Fact]
    public async Task ExportSchedulePackageAsync_InvalidProfile_ReturnsErrorWithoutCallingService()
    {
        var result = await CreateTools().ExportSchedulePackageAsync(
            ["one"],
            "unknown",
            Ct);

        Assert.StartsWith("Error:", result);
        _packages.Verify(service => service.ExportAsync(
            It.IsAny<ScheduledJobPackageExportRequest>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task BuildSchedulePackageAsync_NullDependencyKind_ReturnsError()
    {
        var input = new SchedulePackageJobMcpInput(
            "External",
            "Run",
            null,
            null,
            "daily",
            "05:00",
            [],
            null,
            null,
            null,
            null,
            []);

        var result = await CreateTools().BuildSchedulePackageAsync(
            [input],
            [
                new SchedulePackageDependencyMcpInput(
                    "bad",
                    null!,
                    "Bad",
                    true,
                    null,
                    null,
                    null),
            ],
            "transfer",
            Ct);

        Assert.StartsWith("Error:", result);
        _packages.Verify(service => service.BuildAsync(
            It.IsAny<ScheduledJobPackageBuildRequest>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }
}
