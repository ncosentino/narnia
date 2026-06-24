using System.IO.Abstractions;
using NexusLabs.Narnia.Core.Configuration;

namespace NexusLabs.Narnia.Core.Repositories;

/// <summary>
/// Performs a one-time move of Narnia's settings database from its legacy location
/// (a flat file inside the Copilot-owned <c>~/.copilot</c> directory) to the current
/// per-app location under local application data. Runs before migrations on startup and
/// is safe to invoke every launch: it only acts when the destination is absent and a legacy
/// file is present.
/// </summary>
public sealed class SettingsDatabaseRelocator(NarniaOptions options, IFileSystem fileSystem)
{
    private static readonly string[] SidecarSuffixes = ["-wal", "-shm"];

    /// <summary>
    /// Moves the legacy settings database to the configured <see cref="NarniaOptions.SettingsDatabasePath"/>
    /// when needed. No-op when a connection-string override is set, when the destination already
    /// exists, or when there is no legacy database to move.
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
        // On failure the legacy file is left untouched so the data is never lost, and a later
        // launch retries the move.
        try
        {
            // Copy → verify → delete so an interruption never destroys the only copy. The
            // -wal/-shm sidecars are moved first so any un-checkpointed pages survive the move.
            foreach (var suffix in SidecarSuffixes)
                TryMoveSidecar(legacy + suffix, destination + suffix);

            fileSystem.File.Copy(legacy, destination, overwrite: false);
            if (!fileSystem.File.Exists(destination))
                return;

            TryDelete(legacy);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Drop a partial destination so the migrator never runs against a half-copied file.
            TryDelete(destination);
        }
    }

    private void TryMoveSidecar(string source, string target)
    {
        if (!fileSystem.File.Exists(source))
            return;

        try
        {
            fileSystem.File.Copy(source, target, overwrite: true);
            TryDelete(source);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private void TryDelete(string path)
    {
        try
        {
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
