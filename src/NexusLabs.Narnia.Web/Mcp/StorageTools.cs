using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using NexusLabs.Narnia.Core.Models;
using NexusLabs.Narnia.Core.Services;

namespace NexusLabs.Narnia.Web.Mcp;

[McpServerToolType]
internal sealed class StorageTools(
    ISessionStorageService storageService,
    ISessionCleanupService cleanupService,
    ISessionStorageScanCoordinator scanCoordinator)
{
    [McpServerTool(Name = "get_session_storage_overview")]
    [Description("Gets cached local Copilot session-storage totals, scan health, and the 25 largest local sessions. Sizes are logical bytes and do not include reparse targets.")]
    public async Task<string> GetSessionStorageOverviewAsync(CancellationToken cancellationToken)
    {
        var dashboard = await storageService.GetDashboardAsync(cancellationToken);
        var response = new SessionStorageMcpDto(
            dashboard.Overview,
            dashboard.LastScan,
            dashboard.Sessions
                .Where(item => item.Storage is not null)
                .OrderByDescending(item => item.Storage!.TotalBytes)
                .Take(25)
                .Select(item => new SessionStorageItemMcpDto(
                    item.SessionId,
                    item.Summary,
                    item.Repository,
                    item.Storage!.TotalBytes,
                    item.Storage.GrowthBytes,
                    item.UpdatedAt,
                    item.DataState.ToString(),
                    item.IsActive,
                    item.IsProtected,
                    item.Storage.IsComplete,
                    item.Storage.ContainsGitRepository,
                    item.Storage.ContainsLinkedWorktree,
                    item.Storage.ContainsReparsePoint))
                .ToArray());
        return JsonSerializer.Serialize(response, McpJsonContext.Default.SessionStorageMcpDto);
    }

    [McpServerTool(Name = "preview_local_session_cleanup")]
    [Description("Dry-runs local session deletion. Returns allowed, protected, and hard-blocked sessions plus estimated logical bytes. No data is deleted.")]
    public async Task<string> PreviewLocalSessionCleanupAsync(
        [Description("Copilot session IDs to validate.")] string[] sessionIds,
        [Description("Whether Narnia favorites, aliases, Collections, Session Groups, and user-assigned names may be overridden.")] bool overrideProtections,
        CancellationToken cancellationToken)
    {
        var preview = await cleanupService.PreviewAsync(
            sessionIds,
            overrideProtections,
            cancellationToken);
        return JsonSerializer.Serialize(preview, McpJsonContext.Default.SessionCleanupPreview);
    }

    [McpServerTool(Name = "delete_local_sessions")]
    [Description("Permanently deletes validated local Copilot session data through GitHub.Copilot.SDK and can archive successful deletions in Narnia. Synced GitHub copies and Narnia references remain. Always preview first.")]
    public async Task<string> DeleteLocalSessionsAsync(
        [Description("Copilot session IDs to delete locally.")] string[] sessionIds,
        [Description("Whether default Narnia protections may be overridden.")] bool overrideProtections,
        [Description("Whether successfully deleted sessions should be archived in Narnia and hidden from normal views.")] bool archiveDeletedSessions,
        [Description("Must be true to acknowledge that local deletion is irreversible.")] bool confirmLocalDeletion,
        CancellationToken cancellationToken)
    {
        if (!confirmLocalDeletion)
            return """{"error":"Local session deletion was not explicitly confirmed."}""";

        var result = await cleanupService.DeleteAsync(
            sessionIds,
            overrideProtections,
            archiveDeletedSessions,
            cancellationToken);
        return JsonSerializer.Serialize(result, McpJsonContext.Default.SessionCleanupBatchResult);
    }

    [McpServerTool(Name = "scan_session_storage")]
    [Description("Queues a background metadata-only scan of local Copilot session-state storage.")]
    public Task<string> ScanSessionStorageAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        var accepted = scanCoordinator.RequestScan();
        var response = new SessionStorageScanRequestMcpDto(
            accepted,
            scanCoordinator.GetProgress());
        return Task.FromResult(
            JsonSerializer.Serialize(
                response,
                McpJsonContext.Default.SessionStorageScanRequestMcpDto));
    }
}

internal sealed record SessionStorageMcpDto(
    SessionStorageOverview Overview,
    SessionStorageScanInfo? LastScan,
    SessionStorageItemMcpDto[] LargestSessions);

internal sealed record SessionStorageItemMcpDto(
    string SessionId,
    string? Summary,
    string? Repository,
    long TotalBytes,
    long GrowthBytes,
    DateTimeOffset? UpdatedAt,
    string DataState,
    bool IsActive,
    bool IsProtected,
    bool IsComplete,
    bool ContainsGitRepository,
    bool ContainsLinkedWorktree,
    bool ContainsReparsePoint);

internal sealed record SessionStorageScanRequestMcpDto(
    bool Accepted,
    SessionStorageScanProgress Progress);
