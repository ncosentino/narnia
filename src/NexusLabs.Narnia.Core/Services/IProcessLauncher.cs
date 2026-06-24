namespace NexusLabs.Narnia.Core.Services;

/// <summary>
/// A seam over OS process creation so terminal launching can be unit-tested without spawning
/// real processes. The single concrete implementation starts a detached, shell-executed process.
/// </summary>
public interface IProcessLauncher
{
    /// <summary>
    /// Starts a process for the given executable and arguments.
    /// </summary>
    /// <param name="fileName">The executable to start (e.g. <c>wt.exe</c> or a shell path).</param>
    /// <param name="arguments">The command-line arguments.</param>
    /// <param name="workingDirectory">The working directory, or <c>null</c> to inherit the default.</param>
    /// <exception cref="System.Exception">Thrown when the process cannot be started.</exception>
    void Start(string fileName, string arguments, string? workingDirectory = null);
}
