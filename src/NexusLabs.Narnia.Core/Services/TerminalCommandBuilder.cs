namespace NexusLabs.Narnia.Core.Services;

/// <summary>
/// Default <see cref="ITerminalCommandBuilder"/> targeting Windows Terminal (<c>wt.exe</c>).
/// </summary>
public sealed class TerminalCommandBuilder : ITerminalCommandBuilder
{
    /// <inheritdoc />
    public string? FindWindowsTerminalPath()
    {
        if (!OperatingSystem.IsWindows())
            return null;

        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "WindowsApps", "wt.exe");
        return File.Exists(path) ? path : null;
    }

    /// <inheritdoc />
    public string BuildShellArguments(string shellName, string sessionId, string copilotCommand)
    {
        var resumeCommand = $"{copilotCommand} --resume={sessionId}";
        return shellName switch
        {
            "pwsh" or "powershell" => $"-NoExit -Command \"{resumeCommand}\"",
            "cmd" => $"/k {resumeCommand}",
            _ => $"-c \"{resumeCommand}; exec $SHELL\"",
        };
    }

    /// <inheritdoc />
    public string BuildNewTabSegment(string shellPath, string shellName, TerminalLaunchTab tab, string copilotCommand)
    {
        var safeTitle = tab.Title.Replace("\"", "\\\"");
        var directoryArgument = tab.Directory is not null
            ? $"--startingDirectory \"{tab.Directory}\" "
            : string.Empty;

        return $"new-tab --title \"{safeTitle}\" --suppressApplicationTitle {directoryArgument}-- " +
               $"\"{shellPath}\" {BuildShellArguments(shellName, tab.SessionId, copilotCommand)}";
    }

    /// <inheritdoc />
    public string BuildWindowCommand(
        string shellPath, string shellName, IReadOnlyList<TerminalLaunchTab> tabs, string copilotCommand) =>
        string.Join(
            " ; ",
            tabs.Select(tab => BuildNewTabSegment(
                shellPath,
                shellName,
                tab,
                copilotCommand)));

    /// <inheritdoc />
    public string BuildNewWindowCommand(
        string shellPath,
        string shellName,
        IReadOnlyList<TerminalLaunchTab> tabs,
        string copilotCommand) =>
        $"-w new {BuildWindowCommand(shellPath, shellName, tabs, copilotCommand)}";

    /// <inheritdoc />
    public string BuildDirectLaunchArguments(string shellName, TerminalLaunchTab tab, string copilotCommand)
    {
        var resumeCommand = $"{copilotCommand} --resume={tab.SessionId}";
        var safeTitle = tab.Title.Replace("\"", "\\\"");
        return shellName switch
        {
            "pwsh" or "powershell" =>
                $"-NoExit -Command \"$host.UI.RawUI.WindowTitle = '{tab.Title.Replace("'", "''")}'; {resumeCommand}\"",
            "cmd" => $"/k title {tab.Title} & {resumeCommand}",
            _ => $"-c \"printf '\\033]0;{safeTitle}\\007'; {resumeCommand}; exec $SHELL\"",
        };
    }
}
