using System.IO.Abstractions;
using System.Security;
using NexusLabs.Narnia.Core.Configuration;
using NexusLabs.Narnia.Core.Models;
using NexusLabs.Narnia.Core.Repositories;

namespace NexusLabs.Narnia.Core.Services;

/// <summary>Streams filesystem metadata into cached per-session storage measurements.</summary>
public sealed class SessionStorageScanner(
    NarniaOptions options,
    IFileSystem fileSystem,
    IWorkspaceReader workspaceReader,
    ISessionStorageRepository repository,
    TimeProvider timeProvider) : ISessionStorageScanner
{
    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<SessionStorageRecord>> ScanAsync(
        IProgress<(int Scanned, int Total)> progress,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(progress);
        var startedAt = timeProvider.GetUtcNow();
        if (!fileSystem.Directory.Exists(options.SessionStatePath))
        {
            var error = $"Copilot session-state directory does not exist: {options.SessionStatePath}";
            await repository.RecordScanFailureAsync(
                startedAt,
                timeProvider.GetUtcNow(),
                error,
                ct);
            throw new DirectoryNotFoundException(error);
        }

        string[] sessionDirectories;
        try
        {
            sessionDirectories = fileSystem.Directory
                .GetDirectories(options.SessionStatePath, "*", SearchOption.TopDirectoryOnly);
        }
        catch (Exception exception) when (IsFilesystemException(exception))
        {
            await repository.RecordScanFailureAsync(
                startedAt,
                timeProvider.GetUtcNow(),
                exception.Message,
                ct);
            throw new IOException(
                $"Copilot session-state directories could not be enumerated: {exception.Message}",
                exception);
        }

        Array.Sort(sessionDirectories, StringComparer.OrdinalIgnoreCase);
        var records = new List<SessionStorageRecord>(sessionDirectories.Length);
        for (var index = 0; index < sessionDirectories.Length; index++)
        {
            ct.ThrowIfCancellationRequested();
            records.Add(ScanSession(sessionDirectories[index], startedAt, ct));
            progress.Report((index + 1, sessionDirectories.Length));

            if ((index + 1) % 100 == 0)
                await Task.Yield();
        }

        await repository.SaveScanAsync(
            records,
            startedAt,
            timeProvider.GetUtcNow(),
            ct);
        return records;
    }

    private SessionStorageRecord ScanSession(
        string sessionDirectory,
        DateTimeOffset scannedAt,
        CancellationToken ct)
    {
        var state = new MeasurementState();
        try
        {
            state.IsUserNamed = workspaceReader.ReadMetadata(
                fileSystem.Path.GetFileName(sessionDirectory)).IsUserNamed;
        }
        catch (Exception exception) when (IsFilesystemException(exception))
        {
            state.RecordError(exception.Message);
        }

        try
        {
            var rootAttributes = fileSystem.File.GetAttributes(sessionDirectory);
            if ((rootAttributes & FileAttributes.ReparsePoint) != 0)
            {
                state.ContainsReparsePoint = true;
                state.RecordError("The session-state directory is a reparse point and was not scanned.");
                return CreateRecord(sessionDirectory, scannedAt, state);
            }
        }
        catch (Exception exception) when (IsFilesystemException(exception))
        {
            state.RecordError(exception.Message);
            return CreateRecord(sessionDirectory, scannedAt, state);
        }

        var pending = new Stack<string>();
        pending.Push(sessionDirectory);

        while (pending.TryPop(out var directory))
        {
            ct.ThrowIfCancellationRequested();
            IEnumerable<string> entries;
            try
            {
                entries = fileSystem.Directory.EnumerateFileSystemEntries(directory);
            }
            catch (Exception exception) when (IsFilesystemException(exception))
            {
                state.RecordError(exception.Message);
                continue;
            }

            try
            {
                foreach (var entry in entries)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        var attributes = fileSystem.File.GetAttributes(entry);
                        if ((attributes & FileAttributes.ReparsePoint) != 0)
                        {
                            state.ContainsReparsePoint = true;
                            continue;
                        }

                        if ((attributes & FileAttributes.Directory) != 0)
                        {
                            if (IsGitMarker(entry))
                                state.ContainsGitRepository = true;
                            pending.Push(entry);
                            continue;
                        }

                        var fileInfo = fileSystem.FileInfo.New(entry);
                        var relativePath = fileSystem.Path.GetRelativePath(sessionDirectory, entry);
                        var category = Classify(relativePath);
                        state.AddFile(category, relativePath, fileInfo.Length, fileInfo.LastWriteTimeUtc);
                        if (category == StorageCategory.Artifacts && IsGitMarker(entry))
                            state.ContainsLinkedWorktree = true;
                    }
                    catch (Exception exception) when (IsFilesystemException(exception))
                    {
                        state.RecordError(exception.Message);
                    }
                }
            }
            catch (Exception exception) when (IsFilesystemException(exception))
            {
                state.RecordError(exception.Message);
            }
        }

        return CreateRecord(sessionDirectory, scannedAt, state);
    }

    private SessionStorageRecord CreateRecord(
        string sessionDirectory,
        DateTimeOffset scannedAt,
        MeasurementState state) =>
        new()
        {
            SessionId = fileSystem.Path.GetFileName(sessionDirectory),
            ScannedAt = scannedAt,
            TotalBytes = state.Categories.TotalBytes,
            FileCount = state.FileCount,
            LastWriteAt = state.LastWriteAt,
            EventsBytes = state.Categories.EventsBytes,
            SessionDatabaseBytes = state.Categories.SessionDatabaseBytes,
            CheckpointsBytes = state.Categories.CheckpointsBytes,
            RewindBytes = state.Categories.RewindBytes,
            ArtifactsBytes = state.Categories.ArtifactsBytes,
            OtherBytes = state.Categories.OtherBytes,
            LargestFileBytes = state.LargestFileBytes,
            LargestFilePath = state.LargestFilePath,
            IsComplete = state.Error is null,
            Error = state.Error,
            IsUserNamed = state.IsUserNamed,
            ContainsGitRepository = state.ContainsGitRepository,
            ContainsLinkedWorktree = state.ContainsLinkedWorktree,
            ContainsReparsePoint = state.ContainsReparsePoint,
        };

    private static StorageCategory Classify(string relativePath)
    {
        var firstSegment = relativePath
            .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], 2)
            .FirstOrDefault();
        return firstSegment?.ToLowerInvariant() switch
        {
            "events.jsonl" => StorageCategory.Events,
            "session.db" => StorageCategory.SessionDatabase,
            "checkpoints" => StorageCategory.Checkpoints,
            "rewind-file-snapshots" or "rewind-snapshots" => StorageCategory.Rewind,
            "files" or "research" => StorageCategory.Artifacts,
            _ => StorageCategory.Other,
        };
    }

    private static bool IsGitMarker(string path) =>
        string.Equals(Path.GetFileName(path), ".git", StringComparison.OrdinalIgnoreCase);

    private static bool IsFilesystemException(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or SecurityException;

    private enum StorageCategory
    {
        Events,
        SessionDatabase,
        Checkpoints,
        Rewind,
        Artifacts,
        Other,
    }

    private sealed class MeasurementState
    {
        public long FileCount { get; private set; }
        public DateTimeOffset? LastWriteAt { get; private set; }
        public long LargestFileBytes { get; private set; }
        public string? LargestFilePath { get; private set; }
        public string? Error { get; private set; }
        public bool IsUserNamed { get; set; }
        public bool ContainsGitRepository { get; set; }
        public bool ContainsLinkedWorktree { get; set; }
        public bool ContainsReparsePoint { get; set; }
        public MutableCategoryTotals Categories { get; } = new();

        public void AddFile(
            StorageCategory category,
            string relativePath,
            long bytes,
            DateTime lastWriteTimeUtc)
        {
            FileCount++;
            Categories.Add(category, bytes);
            var lastWrite = new DateTimeOffset(lastWriteTimeUtc, TimeSpan.Zero);
            if (LastWriteAt is null || lastWrite > LastWriteAt)
                LastWriteAt = lastWrite;
            if (bytes <= LargestFileBytes)
                return;

            LargestFileBytes = bytes;
            LargestFilePath = relativePath;
        }

        public void RecordError(string error)
        {
            Error ??= error;
        }
    }

    private sealed class MutableCategoryTotals
    {
        public long EventsBytes { get; private set; }
        public long SessionDatabaseBytes { get; private set; }
        public long CheckpointsBytes { get; private set; }
        public long RewindBytes { get; private set; }
        public long ArtifactsBytes { get; private set; }
        public long OtherBytes { get; private set; }
        public long TotalBytes =>
            EventsBytes +
            SessionDatabaseBytes +
            CheckpointsBytes +
            RewindBytes +
            ArtifactsBytes +
            OtherBytes;

        public void Add(StorageCategory category, long bytes)
        {
            switch (category)
            {
                case StorageCategory.Events:
                    EventsBytes += bytes;
                    break;
                case StorageCategory.SessionDatabase:
                    SessionDatabaseBytes += bytes;
                    break;
                case StorageCategory.Checkpoints:
                    CheckpointsBytes += bytes;
                    break;
                case StorageCategory.Rewind:
                    RewindBytes += bytes;
                    break;
                case StorageCategory.Artifacts:
                    ArtifactsBytes += bytes;
                    break;
                default:
                    OtherBytes += bytes;
                    break;
            }
        }
    }
}
