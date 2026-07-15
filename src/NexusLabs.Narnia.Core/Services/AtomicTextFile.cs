using System.IO.Abstractions;
using System.Text;

namespace NexusLabs.Narnia.Core.Services;

/// <summary>
/// Writes text through a sibling temporary file and replaces the destination only after the full
/// content has been persisted.
/// </summary>
public static class AtomicTextFile
{
    /// <summary>
    /// Atomically replaces a text file without exposing a partially written destination.
    /// </summary>
    /// <param name="fileSystem">Filesystem used for the write and move operations.</param>
    /// <param name="path">Destination path.</param>
    /// <param name="content">Complete text content.</param>
    /// <param name="encoding">Encoding used for the temporary file.</param>
    public static void Write(
        IFileSystem fileSystem,
        string path,
        string content,
        Encoding encoding)
    {
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            fileSystem.File.WriteAllText(temporaryPath, content, encoding);
            fileSystem.File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (fileSystem.File.Exists(temporaryPath))
                fileSystem.File.Delete(temporaryPath);
        }
    }

    /// <summary>
    /// Asynchronously replaces a text file without exposing a partially written destination.
    /// </summary>
    /// <param name="fileSystem">Filesystem used for the write and move operations.</param>
    /// <param name="path">Destination path.</param>
    /// <param name="content">Complete text content.</param>
    /// <param name="encoding">Encoding used for the temporary file.</param>
    /// <param name="ct">Cancellation token for the temporary-file write.</param>
    public static async ValueTask WriteAsync(
        IFileSystem fileSystem,
        string path,
        string content,
        Encoding encoding,
        CancellationToken ct = default)
    {
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await fileSystem.File.WriteAllTextAsync(
                temporaryPath,
                content,
                encoding,
                ct);
            fileSystem.File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (fileSystem.File.Exists(temporaryPath))
                fileSystem.File.Delete(temporaryPath);
        }
    }
}
