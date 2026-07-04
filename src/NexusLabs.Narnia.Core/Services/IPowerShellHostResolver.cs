namespace NexusLabs.Narnia.Core.Services;

/// <summary>
/// Resolves which PowerShell host executable Narnia-owned scheduled jobs should run under.
/// Windows PowerShell 5.1 (<c>powershell.exe</c>) has a long-standing native-argument-escaping bug:
/// a prompt containing an embedded quote character (e.g. a job whose prompt mentions a literal
/// "no changes" notice) can be silently split into extra arguments once the surrounding text is
/// long/complex enough, which <c>copilot -p</c> then rejects outright. PowerShell 7+
/// (<c>pwsh.exe</c>) fixed this, so it is preferred whenever installed; <c>powershell.exe</c> remains
/// the fallback since it ships on every supported Windows version.
/// </summary>
public interface IPowerShellHostResolver
{
    /// <summary>The best available PowerShell host executable name for running a job's wrapper script.</summary>
    string ResolveExecutable();
}

/// <summary>
/// Platform-agnostic fallback that always reports Windows PowerShell. Used on non-Windows platforms
/// (where scheduled jobs are unsupported entirely) so <see cref="IPowerShellHostResolver"/> can
/// still be resolved from dependency injection unconditionally.
/// </summary>
public sealed class DefaultPowerShellHostResolver : IPowerShellHostResolver
{
    /// <inheritdoc />
    public string ResolveExecutable() => "powershell.exe";
}
