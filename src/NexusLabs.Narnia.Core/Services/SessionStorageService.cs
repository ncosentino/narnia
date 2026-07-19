using NexusLabs.Narnia.Core.Models;
using NexusLabs.Narnia.Core.Repositories;

namespace NexusLabs.Narnia.Core.Services;

/// <summary>Merges cached local storage with indexed and Narnia-owned session metadata.</summary>
public sealed class SessionStorageService(
    ISessionStorageRepository storageRepository,
    ISessionRepository sessionRepository,
    ISessionOverridesRepository overridesRepository,
    ISessionGroupsRepository groupsRepository,
    IWorkCollectionsRepository collectionsRepository,
    ICopilotSessionActivityReader activityReader) : ISessionStorageService
{
    /// <inheritdoc />
    public async ValueTask<SessionStorageDashboard> GetDashboardAsync(CancellationToken ct)
    {
        var storageTask = storageRepository.GetCurrentAsync(ct).AsTask();
        var sessionsTask = sessionRepository.ListAllAsync(true, ct).AsTask();
        var overridesTask = overridesRepository.GetAllOverridesAsync(ct).AsTask();
        var groupsTask = groupsRepository.GetAllAsync(ct).AsTask();
        var collectionsTask = collectionsRepository.GetAllAsync(ct).AsTask();
        var historyTask = storageRepository.GetDailyAsync(90, ct).AsTask();
        var cleanupHistoryTask = storageRepository.GetRecentCleanupAsync(25, ct).AsTask();
        var scanTask = storageRepository.GetLastScanAsync(ct).AsTask();
        await Task.WhenAll(
            storageTask,
            sessionsTask,
            overridesTask,
            groupsTask,
            collectionsTask,
            historyTask,
            cleanupHistoryTask,
            scanTask);

        var storage = await storageTask;
        var sessions = await sessionsTask;
        var savedOverrides = await overridesTask;
        var groupedSessionIds = (await groupsTask)
            .SelectMany(group => group.Members)
            .Select(member => member.SessionId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var collectedSessionIds = (await collectionsTask)
            .SelectMany(collection => collection.Members)
            .Select(member => member.SessionId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var activeSessionIds = activityReader.GetActiveSessionIds();
        var sessionsById = sessions.ToDictionary(
            session => session.Id,
            StringComparer.OrdinalIgnoreCase);
        var storageById = storage.ToDictionary(
            record => record.SessionId,
            StringComparer.OrdinalIgnoreCase);

        var allSessionIds = new HashSet<string>(
            sessionsById.Keys,
            StringComparer.OrdinalIgnoreCase);
        allSessionIds.UnionWith(storageById.Keys);

        var items = new List<SessionStorageItem>(allSessionIds.Count);
        foreach (var sessionId in allSessionIds)
        {
            sessionsById.TryGetValue(sessionId, out var session);
            storageById.TryGetValue(sessionId, out var record);
            savedOverrides.TryGetValue(sessionId, out var savedOverride);
            var inGroup = groupedSessionIds.Contains(sessionId);
            var inCollection = collectedSessionIds.Contains(sessionId);
            var hasNarniaMetadata =
                !string.IsNullOrWhiteSpace(savedOverride?.DisplayName) ||
                !string.IsNullOrWhiteSpace(savedOverride?.Notes);
            var protections = BuildProtectionReasons(
                session?.IsFavorite == true || savedOverride?.IsFavorite == true,
                record?.IsUserNamed == true,
                hasNarniaMetadata,
                inGroup,
                inCollection);

            items.Add(new SessionStorageItem
            {
                SessionId = sessionId,
                Summary = session?.Summary,
                Repository = session?.Repository,
                CreatedAt = session?.CreatedAt,
                UpdatedAt = session?.UpdatedAt,
                DataState = session is null
                    ? SessionStorageDataState.LocalStateOnly
                    : record is null
                        ? SessionStorageDataState.IndexedOnly
                        : SessionStorageDataState.IndexedWithLocalState,
                Storage = record,
                IsActive = activeSessionIds.Contains(sessionId),
                IsFavorite = session?.IsFavorite == true || savedOverride?.IsFavorite == true,
                IsArchived = savedOverride?.IsArchived == true,
                HasNarniaMetadata = hasNarniaMetadata,
                IsInSessionGroup = inGroup,
                IsInCollection = inCollection,
                ProtectionReasons = protections,
            });
        }

        items.Sort(static (left, right) =>
        {
            var size = (right.Storage?.TotalBytes ?? 0).CompareTo(left.Storage?.TotalBytes ?? 0);
            return size != 0
                ? size
                : string.Compare(left.SessionId, right.SessionId, StringComparison.OrdinalIgnoreCase);
        });

        var categories = new SessionStorageCategoryTotals(
            storage.Sum(record => record.EventsBytes),
            storage.Sum(record => record.SessionDatabaseBytes),
            storage.Sum(record => record.CheckpointsBytes),
            storage.Sum(record => record.RewindBytes),
            storage.Sum(record => record.ArtifactsBytes),
            storage.Sum(record => record.OtherBytes));
        var overview = new SessionStorageOverview(
            categories,
            storage.Count,
            items.Count(item => item.DataState == SessionStorageDataState.IndexedOnly),
            items.Count(item => item.DataState == SessionStorageDataState.LocalStateOnly),
            items.Count(item => item.IsActive),
            items.Count(item => item.Storage is not null && item.IsProtected),
            storage.Count(record => !record.IsComplete));

        return new SessionStorageDashboard(
            overview,
            items,
            await historyTask,
            await cleanupHistoryTask,
            await scanTask);
    }

    private static IReadOnlyList<string> BuildProtectionReasons(
        bool favorite,
        bool isUserNamed,
        bool hasNarniaMetadata,
        bool inGroup,
        bool inCollection)
    {
        var reasons = new List<string>(4);
        if (favorite)
            reasons.Add("Favorite");
        if (isUserNamed)
            reasons.Add("Named by you in Copilot");
        if (hasNarniaMetadata)
            reasons.Add("Narnia alias or notes");
        if (inGroup)
            reasons.Add("Session Group member");
        if (inCollection)
            reasons.Add("Collection member");
        return reasons;
    }
}
