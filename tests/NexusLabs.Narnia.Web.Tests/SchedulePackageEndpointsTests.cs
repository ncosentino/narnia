using System.Net;
using System.Net.Http.Json;
using NexusLabs.Narnia.Core.Models;
using NexusLabs.Narnia.Core.Services;

namespace NexusLabs.Narnia.Web.Tests;

public sealed class SchedulePackageEndpointsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ExportAndImport_RoundTripsThroughSeparateNarniaInstances_Disabled()
    {
        string packageJson;
        using (var source = new NarniaWebAppFactory())
        {
            var client = source.CreateClient();
            var create = await client.PostAsJsonAsync(
                "/api/schedules",
                new
                {
                    name = "Portable",
                    prompt = "Run the portable task",
                    cadenceKind = "weekly",
                    time = "06:30",
                    days = new[] { "Friday" },
                    register = true,
                },
                Ct);
            create.EnsureSuccessStatusCode();
            var created = await create.Content.ReadFromJsonAsync<CreateResponse>(Ct);

            var export = await client.PostAsJsonAsync(
                "/api/schedule-packages/export",
                new
                {
                    jobIds = new[] { created!.Id },
                    profile = "transfer",
                },
                Ct);
            export.EnsureSuccessStatusCode();
            packageJson = (await export.Content.ReadFromJsonAsync<ExportResponse>(Ct))!.PackageJson!;
        }

        using var target = new NarniaWebAppFactory();
        var targetClient = target.CreateClient();
        var preview = await targetClient.PostAsJsonAsync(
            "/api/schedule-packages/preview",
            new
            {
                packageJson,
                bindings = Array.Empty<object>(),
                jobs = Array.Empty<object>(),
            },
            Ct);
        preview.EnsureSuccessStatusCode();
        var previewBody = await preview.Content.ReadFromJsonAsync<PreviewResponse>(Ct);
        Assert.True(Assert.Single(previewBody!.Jobs).CanImport);

        ScheduledTaskRegistration? registration = null;
        target.ScheduledTaskRegistrar
            .Setup(registrar => registrar.RegisterAsync(
                It.IsAny<ScheduledTaskRegistration>(),
                It.IsAny<CancellationToken>()))
            .Callback<ScheduledTaskRegistration, CancellationToken>((value, _) => registration = value)
            .ReturnsAsync(ScheduledTaskCommandResult.Success);
        var import = await targetClient.PostAsJsonAsync(
            "/api/schedule-packages/import",
            new
            {
                packageJson,
                bindings = Array.Empty<object>(),
                jobs = Array.Empty<object>(),
                previewFingerprint = previewBody.PreviewFingerprint,
            },
            Ct);

        import.EnsureSuccessStatusCode();
        var imported = await import.Content.ReadFromJsonAsync<ImportResponse>(Ct);
        Assert.True(imported!.Ok);
        Assert.False(registration!.Enabled);
        var local = Assert.Single(await target.ScheduledJobRegistry.GetAllAsync(Ct));
        Assert.NotEqual(Assert.Single(imported.Jobs).PortableJobId, local.Id);
        Assert.Equal("Run the portable task", local.Prompt);
    }

    [Fact]
    public async Task Preview_TaskNameConflict_ReturnsNonImportableJob()
    {
        using var factory = new NarniaWebAppFactory();
        var client = factory.CreateClient();
        var build = await client.PostAsJsonAsync(
            "/api/schedule-packages/build",
            new
            {
                profile = "transfer",
                jobs = new[]
                {
                    new
                    {
                        name = "Conflicting",
                        prompt = "Run",
                        cadenceKind = "daily",
                        time = "05:00",
                        taskName = "Conflicting",
                    },
                },
            },
            Ct);
        build.EnsureSuccessStatusCode();
        var packageJson = (await build.Content.ReadFromJsonAsync<ExportResponse>(Ct))!.PackageJson!;
        await client.PostAsJsonAsync(
            "/api/schedules",
            new
            {
                name = "Existing",
                prompt = "Run existing",
                cadenceKind = "daily",
                time = "05:00",
                taskName = "Conflicting",
                register = true,
            },
            Ct);

        var preview = await client.PostAsJsonAsync(
            "/api/schedule-packages/preview",
            new
            {
                packageJson,
                bindings = Array.Empty<object>(),
                jobs = Array.Empty<object>(),
            },
            Ct);

        preview.EnsureSuccessStatusCode();
        var body = await preview.Content.ReadFromJsonAsync<PreviewResponse>(Ct);
        var job = Assert.Single(body!.Jobs);
        Assert.False(job.CanImport);
        Assert.Equal("TaskNameConflict", job.Status);
    }

    [Fact]
    public async Task Import_StalePreview_ReturnsBadRequest()
    {
        using var factory = new NarniaWebAppFactory();
        var client = factory.CreateClient();
        var build = await client.PostAsJsonAsync(
            "/api/schedule-packages/build",
            new
            {
                profile = "share",
                jobs = new[]
                {
                    new
                    {
                        name = "Job",
                        prompt = "Run",
                        cadenceKind = "daily",
                        time = "05:00",
                    },
                },
            },
            Ct);
        var packageJson = (await build.Content.ReadFromJsonAsync<ExportResponse>(Ct))!.PackageJson!;

        var import = await client.PostAsJsonAsync(
            "/api/schedule-packages/import",
            new
            {
                packageJson,
                bindings = Array.Empty<object>(),
                jobs = Array.Empty<object>(),
                previewFingerprint = "stale",
            },
            Ct);

        Assert.Equal(HttpStatusCode.BadRequest, import.StatusCode);
        Assert.Empty(await factory.ScheduledJobRegistry.GetAllAsync(Ct));
    }

    [Fact]
    public async Task Build_NullDependencyKind_ReturnsBadRequest()
    {
        using var factory = new NarniaWebAppFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/schedule-packages/build",
            new
            {
                profile = "transfer",
                jobs = new[]
                {
                    new
                    {
                        name = "Job",
                        prompt = "Run",
                        cadenceKind = "daily",
                        time = "05:00",
                    },
                },
                dependencies = new[]
                {
                    new
                    {
                        id = "bad",
                        kind = (string?)null,
                        name = "Bad",
                        required = true,
                    },
                },
            },
            Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private sealed record CreateResponse(string Id);
    private sealed record ExportResponse(bool Ok, string? PackageJson);
    private sealed record PreviewResponse(bool Ok, string PreviewFingerprint, List<JobPreviewResponse> Jobs);
    private sealed record JobPreviewResponse(string PortableJobId, string Status, bool CanImport);
    private sealed record ImportResponse(bool Ok, List<ImportedJobResponse> Jobs);
    private sealed record ImportedJobResponse(string PortableJobId, string LocalJobId);
}
