using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using NexusLabs.Narnia.Core.Services;

namespace NexusLabs.Narnia.Web.Mcp;

[McpServerToolType]
internal sealed class SessionMigrationTools(ISessionMigrationService migrationService)
{
    [McpServerTool(Name = "preview_session_migration")]
    [Description("Previews whether a Copilot session can be migrated into a valid successor, including recoverable turns, checkpoints, tasks, and Narnia references. Does not modify either session.")]
    public async Task<string> PreviewSessionMigrationAsync(
        [Description("Source Copilot session GUID.")] string sessionId,
        CancellationToken cancellationToken)
    {
        var preview = await migrationService.PreviewAsync(sessionId, cancellationToken);
        return JsonSerializer.Serialize(
            ToDto(preview),
            McpJsonContext.Default.SessionMigrationPreviewMcpDto);
    }

    [McpServerTool(Name = "migrate_broken_session")]
    [Description("Archives a broken event stream and asks Copilot SDK to reseed the same session ID and folder with a recovery handoff. Chronicle is never modified directly.")]
    public async Task<string> MigrateBrokenSessionAsync(
        [Description("Source Copilot session GUID.")] string sessionId,
        [Description("Must be true to confirm creation of a new Copilot session and one bootstrap model response.")] bool confirmMigration,
        CancellationToken cancellationToken)
    {
        if (!confirmMigration)
            return """{"error":"Session migration was not explicitly confirmed."}""";

        var result = await migrationService.MigrateAsync(sessionId, cancellationToken);
        return JsonSerializer.Serialize(
            new SessionMigrationResultMcpDto(
                result.Migrated,
                result.Migration is null ? null : ToDto(result.Migration),
                result.Error),
            McpJsonContext.Default.SessionMigrationResultMcpDto);
    }

    [McpServerTool(Name = "get_session_recovery_packet")]
    [Description("Reads a bounded chunk of the Narnia-owned recovery packet associated with a migrated source or successor session.")]
    public async Task<string> GetSessionRecoveryPacketAsync(
        [Description("Source or recovered successor Copilot session GUID.")] string sessionId,
        [Description("Zero-based character offset.")] int offset,
        [Description("Maximum characters to return, clamped to 50,000.")] int maxCharacters,
        CancellationToken cancellationToken)
    {
        var chunk = await migrationService.ReadPacketAsync(
            sessionId,
            Math.Max(0, offset),
            Math.Clamp(maxCharacters, 1, 50_000),
            cancellationToken);
        return chunk is null
            ? """{"error":"Recovery packet not found."}"""
            : JsonSerializer.Serialize(
                chunk,
                McpJsonContext.Default.SessionRecoveryPacketChunk);
    }

    private static SessionMigrationPreviewMcpDto ToDto(
        NexusLabs.Narnia.Core.Models.SessionMigrationPreview preview) =>
        new(
            preview.SourceSessionId,
            preview.Summary,
            preview.CanMigrate,
            preview.BlockingReason,
            preview.IsActive,
            preview.TurnCount,
            preview.CheckpointCount,
            preview.TodoCount,
            preview.ResumeAssessment.Safety.ToString().ToLowerInvariant(),
            preview.ResumeAssessment.Reason,
            preview.ResumeAssessment.FirstEventType,
            preview.ResumeAssessment.IsNestedAgent,
            preview.References,
            preview.ExistingMigration is null
                ? null
                : ToDto(preview.ExistingMigration));

    private static SessionMigrationMcpDto ToDto(
        NexusLabs.Narnia.Core.Models.SessionMigration migration) =>
        new(
            migration.Id,
            migration.SourceSessionId,
            migration.ReplacementSessionId,
            migration.IsInPlace,
            FormatStatus(migration.Status),
            migration.RecoveryPacketBytes,
            migration.RecoveryPacketTruncated,
            migration.ArchivedEventsPath is null
                ? null
                : Path.GetFileName(migration.ArchivedEventsPath),
            migration.ArchivedEventsSha256,
            migration.Error,
            migration.CreatedAt,
            migration.UpdatedAt,
            migration.CompletedAt);

    private static string FormatStatus(
        NexusLabs.Narnia.Core.Models.SessionMigrationStatus status) =>
        status switch
        {
            NexusLabs.Narnia.Core.Models.SessionMigrationStatus.SessionCreated =>
                "session_created",
            NexusLabs.Narnia.Core.Models.SessionMigrationStatus.CleanupRequired =>
                "cleanup_required",
            _ => status.ToString().ToLowerInvariant(),
        };
}

internal sealed record SessionMigrationPreviewMcpDto(
    string SourceSessionId,
    string? Summary,
    bool CanMigrate,
    string? BlockingReason,
    bool IsActive,
    int TurnCount,
    int CheckpointCount,
    int TodoCount,
    string ResumeSafety,
    string? ResumeReason,
    string? FirstEventType,
    bool IsNestedAgent,
    NexusLabs.Narnia.Core.Models.SessionMigrationReferenceSummary References,
    SessionMigrationMcpDto? ExistingMigration);

internal sealed record SessionMigrationResultMcpDto(
    bool Migrated,
    SessionMigrationMcpDto? Migration,
    string? Error);

internal sealed record SessionMigrationMcpDto(
    string Id,
    string SourceSessionId,
    string ReplacementSessionId,
    bool InPlace,
    string Status,
    long RecoveryPacketBytes,
    bool RecoveryPacketTruncated,
    string? ArchivedEventsFileName,
    string? ArchivedEventsSha256,
    string? Error,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? CompletedAt);
