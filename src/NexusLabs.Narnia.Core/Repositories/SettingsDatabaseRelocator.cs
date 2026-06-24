using System.IO.Abstractions;
using NexusLabs.Narnia.Core.Configuration;

namespace NexusLabs.Narnia.Core.Repositories;

/// <summary>
/// Performs a one-time copy of Narnia's settings database from its legacy location
/// (a flat file inside the Copilot-owned <c>~/.copilot</c> directory) to the current
/// per-app location under local application data. Runs before migrations on startup and
/// is safe to invoke every launch: it only acts when the destination is absent and a legacy
/// file is present.
/// </summary>
/// <remarks>
/// The migration is deliberately <b>non-destructive</b>. The legacy database is copied to the
/// new location and then retired by renaming it to a timestamped <c>.bak</c> file; the original
/// bytes are never deleted. An interrupted or failed migration therefore always leaves a
/// recoverable copy of the user's data behind. The only files this type ever deletes are
/// partial copies it just wrote to the <em>destination</em> — it never deletes anything at the
/// legacy location.
/// </remarks>
public sealed class SettingsDatabaseRelocator(NarniaOptions options, IFileSystem fileSystem)
{
    private static readonly string[] SidecarSuffixes = ["-wal", "-shm"];

    /// <summary>
    /// Copies the legacy settings database to the configured <see cref="NarniaOptions.SettingsDatabasePath"/>
    /// when needed, then retires the legacy file by renaming it to a timestamped backup. No-op when
    /// a connection-string override is set, when the destination already exists, or when there is no
    /// legacy database. The legacy data is never deleted, so the operation can never lose it.
    /// </summary>
    public void RelocateIfNeeded()
    {
        if (options.SettingsConnectionString is not null)
            return;

        var destination = options.SettingsDatabasePath;
        var destinationDirectory = fileSystem.Path.GetDirectoryName(destination);
        if (!string.IsNullOrEmpty(destinationDirectory))
            fileSystem.Directory.CreateDirectory(destinationDirectory);

        if (fileSystem.File.Exists(destination))
            return;

        var legacy = NarniaOptions.GetLegacySettingsDatabasePath();
        if (fileSystem.Path.GetFullPath(legacy) == fileSystem.Path.GetFullPath(destination))
            return;
        if (!fileSystem.File.Exists(legacy))
            return;

        // Best-effort, never fatal: a relocation hiccup (e.g. the legacy database is momentarily
        // locked by an old server still shutting down) must not prevent the app from starting.
        // The legacy bytes are never deleted, so a failed attempt simply retries on a later launch
        // with the user's original data still intact.
        try
        {
            // Copy the database and any -wal/-shm sidecars to the new location. Nothing at the
            // legacy location is touched until the destination copy is confirmed present.
            fileSystem.File.Copy(legacy, destination, overwrite: false);
            foreach (var suffix in SidecarSuffixes)
                TryCopy(legacy + suffix, destination + suffix);

            if (!fileSystem.File.Exists(destination))
            {
                CleanDestination(destination);
                return;
            }

            // Retire the legacy files by renaming them to a timestamped backup. This preserves the
            // original bytes on disk as a recoverable copy while ensuring a later launch finds no
            // legacy file to migrate again.
            var backupSuffix = $".migrated-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}.bak";
            TryRename(legacy, legacy + backupSuffix);
            foreach (var suffix in SidecarSuffixes)
                TryRename(legacy + suffix, legacy + suffix + backupSuffix);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Remove any partial copy we just wrote to the destination so the migrator never runs
            // against a half-copied file. The legacy data is left untouched.
            CleanDestination(destination);
        }
    }

    private void TryCopy(string source, string target)
    {
        if (!fileSystem.File.Exists(source))
            return;

        try
        {
            fileSystem.File.Copy(source, target, overwrite: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private void TryRename(string source, string target)
    {
        if (!fileSystem.File.Exists(source))
            return;

        try
        {
            fileSystem.File.Move(source, target);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private void CleanDestination(string destination)
    {
        TryDeleteDestination(destination);
        foreach (var suffix in SidecarSuffixes)
            TryDeleteDestination(destination + suffix);
    }

    private void TryDeleteDestination(string path)
    {
        try
        {
            if (fileSystem.File.Exists(path))
                fileSystem.File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
