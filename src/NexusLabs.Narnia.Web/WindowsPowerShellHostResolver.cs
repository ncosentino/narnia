using System.IO.Abstractions;
using System.Runtime.Versioning;
using NexusLabs.Narnia.Core.Services;

namespace NexusLabs.Narnia.Web;

/// <summary>
/// Resolves <c>pwsh.exe</c> (PowerShell 7+) from <c>PATH</c> when installed, falling back to
/// <c>powershell.exe</c> (Windows PowerShell 5.1, which ships on every supported Windows version)
/// otherwise. See <see cref="IPowerShellHostResolver"/> for why PowerShell 7 is preferred.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsPowerShellHostResolver(IFileSystem fileSystem) : IPowerShellHostResolver
{
    /// <inheritdoc />
    public string ResolveExecutable() =>
        PathExecutableLocator.Find("pwsh.exe", Environment.GetEnvironmentVariable("PATH"), fileSystem) is not null
            ? "pwsh.exe"
            : "powershell.exe";
}
