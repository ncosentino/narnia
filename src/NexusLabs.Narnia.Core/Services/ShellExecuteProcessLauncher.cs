using System.Diagnostics;

namespace NexusLabs.Narnia.Core.Services;

/// <summary>
/// Default <see cref="IProcessLauncher"/> that starts a shell-executed process so the launched
/// terminal survives independently of the Narnia server.
/// </summary>
public sealed class ShellExecuteProcessLauncher : IProcessLauncher
{
    /// <inheritdoc />
    public void Start(string fileName, string arguments, string? workingDirectory = null)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = true,
        };

        if (!string.IsNullOrEmpty(workingDirectory))
            startInfo.WorkingDirectory = workingDirectory;

        Process.Start(startInfo);
    }
}
