using System.IO.Abstractions;
using System.Text;
using System.Text.Json;
using NexusLabs.Narnia.Core.Configuration;
using NexusLabs.Narnia.Core.Models;
using NexusLabs.Narnia.Core.Repositories;

namespace NexusLabs.Narnia.Core.Services;

/// <summary>Filesystem-backed reader and repairer for Copilot's sidebar tab lists.</summary>
public sealed class CopilotSidebarTabsService(
    NarniaOptions options,
    IFileSystem fileSystem,
    ISessionRepository sessions,
    ICopilotSessionActivityReader activityReader,
    TimeProvider timeProvider) : ICopilotSidebarTabsService
{
    /// <summary>
    /// Copilot reads this file with Node and calls <c>JSON.parse</c>, which rejects a leading
    /// byte-order mark. Writing one would leave Copilot unable to read its own sidebar state.
    /// </summary>
    private static readonly UTF8Encoding FileEncoding = new(encoderShouldEmitUTF8Identifier: false);

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<CopilotSidebarWorkspace>> ListAsync(CancellationToken ct)
    {
        var documents = ReadAllDocuments();
        if (documents.Count == 0)
            return [];

        var enriched = await EnrichAsync(documents, ct);
        return [.. enriched
            .OrderByDescending(workspace => workspace.TabCount)
            .ThenBy(workspace => workspace.Cwd, StringComparer.OrdinalIgnoreCase)];
    }

    /// <inheritdoc />
    public async ValueTask<CopilotSidebarWorkspace?> GetAsync(string cwd, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cwd);
        var document = ReadDocument(BuildPath(cwd));
        if (document is null)
            return null;

        var enriched = await EnrichAsync([document], ct);
        return enriched.Count == 0 ? null : enriched[0];
    }

    /// <inheritdoc />
    public ValueTask<CopilotSidebarRepairResult> RemoveTabsAsync(
        string cwd,
        IReadOnlyCollection<string> sessionIds,
        bool force,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cwd);
        ArgumentNullException.ThrowIfNull(sessionIds);

        var removals = sessionIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return removals.Count == 0
            ? ValueTask.FromResult(Failure(cwd, "No sessions were selected for removal."))
            : RepairAsync(cwd, tab => !removals.Contains(tab), force, ct);
    }

    /// <inheritdoc />
    public ValueTask<CopilotSidebarRepairResult> ResetAsync(
        string cwd,
        bool force,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cwd);
        return RepairAsync(cwd, _ => false, force, ct);
    }

    private async ValueTask<CopilotSidebarRepairResult> RepairAsync(
        string cwd,
        Func<string, bool> keep,
        bool force,
        CancellationToken ct)
    {
        var path = BuildPath(cwd);
        var document = ReadDocument(path);
        if (document is null)
            return Failure(cwd, $"No sidebar tab list exists for {cwd}.");
        if (document.ParseError is not null)
            return Failure(cwd, document.ParseError);

        if (!force)
        {
            var active = activityReader.GetActiveSessionIds();
            var live = document.SessionIds.Where(active.Contains).ToArray();
            if (live.Length > 0)
            {
                // Copilot merges its in-memory tab list back over this file during shutdown, so a
                // repair applied underneath a running session is silently reverted on exit.
                return Failure(
                    cwd,
                    $"Copilot is still running {live.Length} session(s) in this folder " +
                    "and rewrites the tab list when it exits. Close those sessions first, " +
                    "or repair with force to overwrite anyway.");
            }
        }

        var retained = document.SessionIds.Where(keep).ToArray();
        if (retained.Length == document.SessionIds.Count)
            return Failure(cwd, "The sidebar tab list already excludes every selected session.");

        string backupPath;
        try
        {
            backupPath = WriteBackup(path);
        }
        catch (Exception exception) when (IsFilesystemException(exception))
        {
            return Failure(cwd, $"The tab list could not be backed up: {exception.Message}");
        }

        try
        {
            var payload = JsonSerializer.Serialize(
                new SidebarStateDocument
                {
                    SchemaVersion = document.SchemaVersion ?? 1,
                    Cwd = document.Cwd,
                    SessionIds = retained,
                },
                SidebarStateJsonContext.Default.SidebarStateDocument);
            // Copilot writes this file with LF, and System.Text.Json indents with the platform
            // newline. Normalizing keeps a repaired file byte-comparable with a Copilot-written one.
            payload = payload.ReplaceLineEndings("\n");
            await AtomicTextFile.WriteAsync(fileSystem, path, payload, FileEncoding, ct);
        }
        catch (Exception exception) when (IsFilesystemException(exception))
        {
            return Failure(cwd, $"The tab list could not be rewritten: {exception.Message}");
        }

        var removed = document.SessionIds.Where(id => !keep(id)).ToArray();
        return new CopilotSidebarRepairResult(
            document.Cwd,
            true,
            removed,
            retained.Length,
            backupPath,
            null);
    }

    /// <summary>
    /// Copies the current tab list beside itself before any rewrite. The backup is timestamped
    /// rather than a fixed <c>.bak</c> so repeated repairs never destroy the first known-good copy.
    /// </summary>
    private string WriteBackup(string path)
    {
        var stamp = timeProvider.GetUtcNow().ToString("yyyyMMddHHmmss");
        var backupPath = $"{path}.{stamp}.narnia-bak";
        fileSystem.File.Copy(path, backupPath, overwrite: true);
        return backupPath;
    }

    private async ValueTask<IReadOnlyList<CopilotSidebarWorkspace>> EnrichAsync(
        IReadOnlyList<SidebarStateFile> documents,
        CancellationToken ct)
    {
        var ids = documents
            .SelectMany(document => document.SessionIds)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        IReadOnlyDictionary<string, Session> known = ids.Length == 0
            ? new Dictionary<string, Session>()
            : await sessions.GetByIdsAsync(ids, ct);
        var active = activityReader.GetActiveSessionIds();

        return [.. documents.Select(document => new CopilotSidebarWorkspace(
            document.Cwd,
            document.Path,
            fileSystem.Path.GetFileName(document.Path),
            document.SchemaVersion,
            [.. document.SessionIds.Select((sessionId, index) =>
            {
                known.TryGetValue(sessionId, out var session);
                return new CopilotSidebarTab(
                    sessionId,
                    index,
                    session is not null,
                    session?.Summary,
                    session?.Repository,
                    active.Contains(sessionId),
                    MeasureEventStream(sessionId));
            })],
            document.Cwd.Length > 0 && fileSystem.Directory.Exists(document.Cwd),
            document.LastWrittenAt,
            document.ParseError))];
    }

    /// <summary>
    /// Reports the session's event-stream size, which is the practical driver of how much content
    /// Copilot's sidebar preview has to render for a tab.
    /// </summary>
    private long? MeasureEventStream(string sessionId)
    {
        try
        {
            var path = fileSystem.Path.Combine(
                options.SessionStatePath,
                sessionId,
                "events.jsonl");
            var file = fileSystem.FileInfo.New(path);
            return file.Exists ? file.Length : null;
        }
        catch (Exception exception) when (IsFilesystemException(exception))
        {
            return null;
        }
    }

    private List<SidebarStateFile> ReadAllDocuments()
    {
        if (!fileSystem.Directory.Exists(options.SidebarStatePath))
            return [];

        string[] paths;
        try
        {
            paths = fileSystem.Directory.GetFiles(
                options.SidebarStatePath,
                $"*{CopilotSidebarStatePath.Extension}",
                SearchOption.TopDirectoryOnly);
        }
        catch (Exception exception) when (IsFilesystemException(exception))
        {
            return [];
        }

        var documents = new List<SidebarStateFile>(paths.Length);
        foreach (var path in paths)
        {
            var document = ReadDocument(path);
            if (document is not null)
                documents.Add(document);
        }

        return documents;
    }

    private string BuildPath(string cwd) =>
        fileSystem.Path.Combine(
            options.SidebarStatePath,
            CopilotSidebarStatePath.FileNameFor(cwd));

    /// <summary>
    /// Reads one state file. A file Narnia cannot parse is surfaced as a workspace carrying a
    /// parse error rather than dropped, because an unreadable tab list is itself a repair target.
    /// </summary>
    private SidebarStateFile? ReadDocument(string path)
    {
        DateTimeOffset? lastWrittenAt = null;
        try
        {
            var file = fileSystem.FileInfo.New(path);
            if (!file.Exists)
                return null;
            lastWrittenAt = file.LastWriteTimeUtc;

            var content = fileSystem.File.ReadAllText(path);
            var parsed = JsonSerializer.Deserialize(
                content,
                SidebarStateJsonContext.Default.SidebarStateDocument);
            if (parsed is null)
                return Unreadable(path, lastWrittenAt, "The tab list is empty or not an object.");

            var cwd = parsed.Cwd ?? string.Empty;
            var sessionIds = (parsed.SessionIds ?? [])
                .Where(sessionId => !string.IsNullOrWhiteSpace(sessionId))
                .ToArray();

            return new SidebarStateFile(
                path,
                cwd,
                parsed.SchemaVersion,
                sessionIds,
                lastWrittenAt,
                null);
        }
        catch (JsonException exception)
        {
            return Unreadable(path, lastWrittenAt, $"The tab list is not valid JSON: {exception.Message}");
        }
        catch (Exception exception) when (IsFilesystemException(exception))
        {
            return Unreadable(path, lastWrittenAt, $"The tab list could not be read: {exception.Message}");
        }
    }

    private SidebarStateFile Unreadable(
        string path,
        DateTimeOffset? lastWrittenAt,
        string error) =>
        new(path, string.Empty, null, [], lastWrittenAt, error);

    private static CopilotSidebarRepairResult Failure(string cwd, string error) =>
        new(cwd, false, [], 0, null, error);

    private static bool IsFilesystemException(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or NotSupportedException
            or ArgumentException;

    private sealed record SidebarStateFile(
        string Path,
        string Cwd,
        int? SchemaVersion,
        IReadOnlyList<string> SessionIds,
        DateTimeOffset? LastWrittenAt,
        string? ParseError);
}
