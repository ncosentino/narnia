using Microsoft.Data.Sqlite;
using System.IO.Abstractions;
using NexusLabs.Narnia.Core.Configuration;
using NexusLabs.Narnia.Core.Models;
using NexusLabs.Narnia.Core.Repositories;

namespace NexusLabs.Narnia.Core.Services;

/// <summary>Applies Narnia's cleanup protections before invoking supported Copilot deletion.</summary>
public sealed class SessionCleanupService(
    ISessionStorageService storageService,
    ISessionStorageRepository storageRepository,
    ISessionOverridesRepository overridesRepository,
    IWorkspaceReader workspaceReader,
    IGitArtifactInspector gitInspector,
    ICopilotSessionManager copilotSessionManager,
    ISessionOperationCoordinator operationCoordinator,
    NarniaOptions options,
    IFileSystem fileSystem,
    TimeProvider timeProvider) : ISessionCleanupService
{
    /// <inheritdoc />
    public async ValueTask<SessionCleanupPreview> PreviewAsync(
        IReadOnlyCollection<string> sessionIds,
        bool overrideProtections,
        CancellationToken ct)
    {
        var normalized = sessionIds
            .Where(sessionId => !string.IsNullOrWhiteSpace(sessionId))
            .Select(sessionId => sessionId.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (normalized.Length == 0)
            return new SessionCleanupPreview([], 0, 0, 0, 0, 0);

        var dashboard = await storageService.GetDashboardAsync(ct);
        var items = dashboard.Sessions.ToDictionary(
            item => item.SessionId,
            StringComparer.OrdinalIgnoreCase);
        var decisions = new List<SessionCleanupDecision>(normalized.Length);
        foreach (var sessionId in normalized)
        {
            ct.ThrowIfCancellationRequested();
            if (!items.TryGetValue(sessionId, out var item))
            {
                decisions.Add(Blocked(sessionId, null, 0, "Session is not known to Narnia."));
                continue;
            }

            decisions.Add(await ValidateAsync(item, overrideProtections, ct));
        }

        return BuildPreview(decisions);
    }

    /// <inheritdoc />
    public async ValueTask<SessionCleanupBatchResult> DeleteAsync(
        IReadOnlyCollection<string> sessionIds,
        bool overrideProtections,
        bool archiveDeletedSessions,
        CancellationToken ct)
    {
        var normalized = sessionIds
            .Where(sessionId => !string.IsNullOrWhiteSpace(sessionId))
            .Select(sessionId => sessionId.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        await using var operation = await operationCoordinator.AcquireAsync(normalized, ct);
        var requestedAt = timeProvider.GetUtcNow();
        var preview = await PreviewAsync(normalized, overrideProtections, ct);
        var allowed = preview.Decisions
            .Where(decision => decision.Disposition == SessionCleanupDisposition.Allowed)
            .ToArray();
        var sdkResults = allowed.Length == 0
            ? []
            : await copilotSessionManager.DeleteSessionsAsync(
                allowed.Select(decision => decision.SessionId).ToArray(),
                ct);
        var sdkById = sdkResults.ToDictionary(
            result => result.SessionId,
            StringComparer.OrdinalIgnoreCase);

        var completedAt = timeProvider.GetUtcNow();
        var results = new List<SessionCleanupResult>(preview.Decisions.Count);
        var audits = new List<SessionCleanupAuditEntry>(preview.Decisions.Count);
        var deletedIds = new List<string>();
        foreach (var decision in preview.Decisions)
        {
            var deleted = false;
            var archived = false;
            string? error = null;
            var resultName = decision.Disposition switch
            {
                SessionCleanupDisposition.Protected => "protected",
                SessionCleanupDisposition.Blocked => "blocked",
                _ => "failed",
            };
            if (decision.Disposition == SessionCleanupDisposition.Allowed)
            {
                if (sdkById.TryGetValue(decision.SessionId, out var sdkResult))
                {
                    deleted = sdkResult.Deleted;
                    error = sdkResult.Error;
                }
                else
                {
                    error = "Copilot SDK did not return a result for this session.";
                }

                resultName = deleted ? "deleted" : "failed";
                if (deleted)
                {
                    deletedIds.Add(decision.SessionId);
                    if (archiveDeletedSessions)
                    {
                        try
                        {
                            await overridesRepository.SetArchivedAsync(
                                decision.SessionId,
                                true,
                                completedAt,
                                ct);
                            archived = true;
                            resultName = "deleted_archived";
                        }
                        catch (SqliteException exception)
                        {
                            error =
                                $"Local data was deleted, but Narnia could not archive the session: {exception.Message}";
                            resultName = "deleted_archive_failed";
                        }
                    }
                }
            }

            results.Add(new SessionCleanupResult(
                decision.SessionId,
                deleted,
                archived,
                decision.EstimatedBytes,
                decision.Reasons,
                error));
            audits.Add(new SessionCleanupAuditEntry(
                Guid.NewGuid().ToString(),
                decision.SessionId,
                requestedAt,
                completedAt,
                decision.EstimatedBytes,
                resultName,
                error));
        }

        await storageRepository.RecordCleanupAsync(audits, ct);
        await storageRepository.RemoveCurrentAsync(deletedIds, ct);
        return new SessionCleanupBatchResult(results);
    }

    private async ValueTask<SessionCleanupDecision> ValidateAsync(
        SessionStorageItem item,
        bool overrideProtections,
        CancellationToken ct)
    {
        var estimatedBytes = item.Storage?.TotalBytes ?? 0;
        if (item.IsActive)
            return Blocked(item, "Session is currently owned by a live Copilot process.");
        if (item.DataState == SessionStorageDataState.IndexedOnly)
            return Blocked(item, "Session has no local state to reclaim.");
        if (item.DataState == SessionStorageDataState.LocalStateOnly)
            return Blocked(item, "Local state is not indexed; run /chronicle reindex before cleanup.");
        if (item.Storage is null)
            return Blocked(item, "No local storage measurement is available.");
        if (!item.Storage.IsComplete)
            return Blocked(item, "The latest storage scan was incomplete.");
        if (!TryResolveSessionDirectory(item.SessionId, out var sessionDirectory))
            return Blocked(item, "Session identifier does not resolve beneath the configured session-state directory.");
        if (!fileSystem.Directory.Exists(sessionDirectory))
            return Blocked(item, "Local session-state directory no longer exists.");

        WorkspaceInfo workspace;
        try
        {
            workspace = workspaceReader.ReadMetadata(item.SessionId);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return Blocked(item, $"Workspace metadata could not be inspected: {exception.Message}");
        }

        var protections = item.ProtectionReasons.ToList();
        if (workspace.IsUserNamed)
            protections.Add("Named by you in Copilot");

        var git = await gitInspector.InspectAsync(sessionDirectory, ct);
        if (!git.IsSafe)
        {
            return new SessionCleanupDecision(
                item.SessionId,
                item.Summary,
                estimatedBytes,
                SessionCleanupDisposition.Blocked,
                git.Reasons);
        }

        if (protections.Count > 0 && !overrideProtections)
        {
            return new SessionCleanupDecision(
                item.SessionId,
                item.Summary,
                estimatedBytes,
                SessionCleanupDisposition.Protected,
                protections);
        }

        return new SessionCleanupDecision(
            item.SessionId,
            item.Summary,
            estimatedBytes,
            SessionCleanupDisposition.Allowed,
            protections);
    }

    private bool TryResolveSessionDirectory(string sessionId, out string sessionDirectory)
    {
        var root = fileSystem.Path.GetFullPath(options.SessionStatePath);
        sessionDirectory = fileSystem.Path.GetFullPath(
            fileSystem.Path.Combine(root, sessionId));
        return string.Equals(
            fileSystem.Path.GetDirectoryName(sessionDirectory),
            root,
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);
    }

    private static SessionCleanupDecision Blocked(
        SessionStorageItem item,
        string reason) =>
        Blocked(item.SessionId, item.Summary, item.Storage?.TotalBytes ?? 0, reason);

    private static SessionCleanupDecision Blocked(
        string sessionId,
        string? summary,
        long estimatedBytes,
        string reason) =>
        new(
            sessionId,
            summary,
            estimatedBytes,
            SessionCleanupDisposition.Blocked,
            [reason]);

    private static SessionCleanupPreview BuildPreview(
        IReadOnlyList<SessionCleanupDecision> decisions)
    {
        var allowed = decisions
            .Where(decision => decision.Disposition == SessionCleanupDisposition.Allowed)
            .ToArray();
        var protectedSessions = decisions
            .Where(decision => decision.Disposition == SessionCleanupDisposition.Protected)
            .ToArray();
        return new SessionCleanupPreview(
            decisions,
            allowed.Length,
            allowed.Sum(decision => decision.EstimatedBytes),
            protectedSessions.Length,
            protectedSessions.Sum(decision => decision.EstimatedBytes),
            decisions.Count(decision => decision.Disposition == SessionCleanupDisposition.Blocked));
    }
}
