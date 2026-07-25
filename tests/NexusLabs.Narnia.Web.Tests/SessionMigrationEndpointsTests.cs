using System.Net;
using System.Net.Http.Json;
using NexusLabs.Narnia.Core.Models;

namespace NexusLabs.Narnia.Web.Tests;

public sealed class SessionMigrationEndpointsTests
{
    private const string SourceId = "11111111-1111-4111-8111-111111111111";
    private const string ReplacementId = "22222222-2222-4222-8222-222222222222";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Preview_ReturnsCompatibilityAndRecoveryCounts()
    {
        using var factory = new NarniaWebAppFactory();
        factory.SessionMigrationService
            .Setup(service => service.PreviewAsync(
                SourceId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Preview());

        var response = await factory.CreateClient().GetAsync(
            $"/api/sessions/{SourceId}/migration",
            Ct);
        var body = await response.Content.ReadFromJsonAsync<PreviewResponse>(Ct);

        response.EnsureSuccessStatusCode();
        Assert.NotNull(body);
        Assert.True(body!.CanMigrate);
        Assert.Equal(101, body.TurnCount);
        Assert.Equal(5, body.CheckpointCount);
        Assert.Equal(121, body.TodoCount);
        Assert.Equal("incompatible", body.ResumeAssessment.Safety);
        Assert.True(body.ResumeAssessment.IsNestedAgent);
    }

    [Fact]
    public async Task Migrate_RequiresExplicitConfirmation()
    {
        using var factory = new NarniaWebAppFactory();

        var response = await factory.CreateClient().PostAsJsonAsync(
            $"/api/sessions/{SourceId}/migration",
            new { confirmMigration = false },
            Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        factory.SessionMigrationService.Verify(service => service.MigrateAsync(
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Migrate_ReturnsRecoveredSuccessor()
    {
        using var factory = new NarniaWebAppFactory();
        var migration = Migration();
        factory.SessionMigrationService
            .Setup(service => service.MigrateAsync(
                SourceId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SessionMigrationResult(true, migration, null));

        var response = await factory.CreateClient().PostAsJsonAsync(
            $"/api/sessions/{SourceId}/migration",
            new { confirmMigration = true },
            Ct);
        var body = await response.Content.ReadFromJsonAsync<MigrationResponse>(Ct);

        response.EnsureSuccessStatusCode();
        Assert.NotNull(body);
        Assert.True(body!.Migrated);
        Assert.Equal(ReplacementId, body.ReplacementSessionId);
    }

    [Fact]
    public async Task MigrationJavaScript_DisablesActionAndNavigatesToSuccessor()
    {
        var javascript = await File.ReadAllTextAsync(FindJavaScript(), Ct);

        Assert.Contains("narniaMigrateSession", javascript, StringComparison.Ordinal);
        Assert.Contains("confirmMigration: true", javascript, StringComparison.Ordinal);
        Assert.Contains("Recovering session in place", javascript, StringComparison.Ordinal);
        Assert.Contains("body.replacementSessionId", javascript, StringComparison.Ordinal);
    }

    private static SessionMigrationPreview Preview() =>
        new(
            SourceId,
            "Pitcrew",
            new SessionResumeAssessment(
                SourceId,
                SessionResumeSafety.Incompatible,
                "Missing session.start.",
                "system.message",
                true),
            false,
            101,
            5,
            121,
            new SessionMigrationReferenceSummary(true, true, true, 0, 0, 0),
            null,
            null);

    private static SessionMigration Migration()
    {
        var now = DateTimeOffset.UtcNow;
        return new SessionMigration(
            "migration-1",
            SourceId,
            ReplacementId,
            SessionMigrationStatus.Completed,
            @"C:\narnia\recovery.md",
            1024,
            false,
            null,
            now,
            now,
            now);
    }

    private static string FindJavaScript()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "src",
                "NexusLabs.Narnia.Web",
                "wwwroot",
                "js",
                "narnia-charts.js");
            if (File.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate narnia-charts.js.");
    }

    private sealed record PreviewResponse(
        bool CanMigrate,
        int TurnCount,
        int CheckpointCount,
        int TodoCount,
        ResumeAssessmentResponse ResumeAssessment);

    private sealed record ResumeAssessmentResponse(
        string Safety,
        bool IsNestedAgent);

    private sealed record MigrationResponse(
        bool Migrated,
        string ReplacementSessionId);
}
