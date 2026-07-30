using System.Text;

namespace NexusLabs.Narnia.Core.Services;

/// <summary>
/// Defines the Windows Task Scheduler entry that starts the Narnia server at logon, and builds the
/// PowerShell commands that register, query, and remove it.
/// </summary>
/// <remarks>
/// The task deliberately lives in its own folder rather than <c>\Narnia\</c>: the Schedules page
/// lists that folder and reports anything without a catalog entry as an orphaned task, so a server
/// task placed there would be reported as stray work.
/// </remarks>
public static class LogonAutostartTask
{
    /// <summary>Task Scheduler folder holding Narnia's own infrastructure tasks.</summary>
    public const string Folder = @"\Narnia\System\";

    /// <summary>Name of the logon autostart task within <see cref="Folder"/>.</summary>
    public const string Name = "Narnia Server Autostart";

    /// <summary>Description marker identifying the task as Narnia's autostart entry.</summary>
    public const string Marker = "narnia-autostart";

    /// <summary>
    /// Delay applied after logon so the server starts once the session has settled rather than
    /// competing with the rest of the sign-in work.
    /// </summary>
    public static readonly TimeSpan LogonDelay = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Builds the script that registers (or replaces) the logon autostart task.
    /// </summary>
    /// <param name="userId">Account the trigger and principal are bound to, such as <c>DOMAIN\user</c>.</param>
    /// <param name="execute">Executable the task runs.</param>
    /// <param name="arguments">Arguments passed to <paramref name="execute"/>.</param>
    /// <param name="workingDirectory">Working directory assigned to the action.</param>
    /// <returns>A self-contained PowerShell command.</returns>
    public static string BuildRegisterScript(
        string userId,
        string execute,
        string arguments,
        string workingDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(execute);

        var delay = $"PT{(int)LogonDelay.TotalSeconds}S";
        var builder = new StringBuilder();

        builder
            .Append("$action = New-ScheduledTaskAction -Execute '").Append(Escape(execute))
            .Append("' -Argument '").Append(Escape(arguments)).Append('\'');
        if (!string.IsNullOrWhiteSpace(workingDirectory))
            builder.Append(" -WorkingDirectory '").Append(Escape(workingDirectory)).Append('\'');
        builder.Append("; ");

        builder
            .Append("$trigger = New-ScheduledTaskTrigger -AtLogOn -User '")
            .Append(Escape(userId)).Append("'; ");
        // Task Scheduler exposes the logon delay only on the trigger object, not as a cmdlet switch.
        builder.Append("$trigger.Delay = '").Append(delay).Append("'; ");

        builder
            .Append("$principal = New-ScheduledTaskPrincipal -UserId '").Append(Escape(userId))
            .Append("' -LogonType Interactive -RunLevel Limited; ");

        // ExecutionTimeLimit zero keeps Task Scheduler from terminating a long-running server, and
        // IgnoreNew keeps a second logon from racing a server that is already listening.
        builder
            .Append("$settings = New-ScheduledTaskSettingsSet")
            .Append(" -MultipleInstances IgnoreNew")
            .Append(" -AllowStartIfOnBatteries")
            .Append(" -DontStopIfGoingOnBatteries")
            .Append(" -StartWhenAvailable")
            .Append(" -ExecutionTimeLimit ([TimeSpan]::Zero)")
            .Append(" -RestartInterval (New-TimeSpan -Minutes 1)")
            .Append(" -RestartCount 3; ");

        builder
            .Append("Register-ScheduledTask -TaskName '").Append(Escape(Name))
            .Append("' -TaskPath '").Append(Escape(Folder))
            .Append("' -Action $action -Trigger $trigger -Principal $principal -Settings $settings")
            .Append(" -Description '").Append(Escape(Marker)).Append('\'')
            .Append(" -Force");

        return builder.ToString();
    }

    /// <summary>
    /// Builds the script that reports whether the autostart task is registered, emitting
    /// <c>true</c> or <c>false</c>.
    /// </summary>
    public static string BuildExistsScript() =>
        "$task = Get-ScheduledTask -TaskName '" + Escape(Name) +
        "' -TaskPath '" + Escape(Folder) +
        "' -ErrorAction SilentlyContinue; " +
        "if ($null -ne $task) { 'true' } else { 'false' }";

    /// <summary>Builds the script that removes the autostart task when present.</summary>
    public static string BuildRemoveScript() =>
        "$task = Get-ScheduledTask -TaskName '" + Escape(Name) +
        "' -TaskPath '" + Escape(Folder) +
        "' -ErrorAction SilentlyContinue; " +
        "if ($null -ne $task) { Unregister-ScheduledTask -TaskName '" + Escape(Name) +
        "' -TaskPath '" + Escape(Folder) + "' -Confirm:$false }";

    private static string Escape(string value) => value.Replace("'", "''");
}
