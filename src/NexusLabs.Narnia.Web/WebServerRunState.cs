using System.Text.Json;

namespace NexusLabs.Narnia.Web;

/// <summary>
/// Reads and writes the Narnia web-server run-state file. The running server owns this file: it
/// writes it once it is listening and removes it on graceful shutdown, so any process can
/// discover a server another session started.
/// </summary>
internal static class WebServerRunState
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    /// <summary>
    /// Absolute path to the run-state file: <c>%LOCALAPPDATA%/narnia/web-server.json</c> on
    /// Windows and the platform-equivalent per-user data directory elsewhere.
    /// </summary>
    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "narnia",
        "web-server.json");

    /// <summary>Writes the run-state file, creating the parent directory if needed.</summary>
    public static void Write(WebServerRunStateInfo info)
    {
        var directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(FilePath, JsonSerializer.Serialize(info, SerializerOptions));
    }

    /// <summary>Best-effort removal of the run-state file; a stale file is tolerated by readers
    /// because they re-verify the recorded PID is alive.</summary>
    public static void Delete()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                File.Delete(FilePath);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
