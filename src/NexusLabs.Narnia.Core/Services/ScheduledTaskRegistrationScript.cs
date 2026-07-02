using System.Text;
using NexusLabs.Narnia.Core.Models;

namespace NexusLabs.Narnia.Core.Services;

/// <summary>
/// Builds the PowerShell command that registers a standardized scheduled task. The same script is
/// run for "register it for me" and shown verbatim for "copy &amp; paste", so the two setup modes
/// can never diverge. Pure and string-only, so it is unit-testable without a scheduler.
/// </summary>
public static class ScheduledTaskRegistrationScript
{
    /// <summary>Builds the registration script for the given task.</summary>
    public static string Build(ScheduledTaskRegistration reg)
    {
        var sb = new StringBuilder();
        sb.Append("$action = New-ScheduledTaskAction -Execute '").Append(Esc(reg.Execute)).Append('\'');
        sb.Append(" -Argument '").Append(Esc(reg.Arguments)).Append('\'');
        if (!string.IsNullOrWhiteSpace(reg.WorkingDirectory))
            sb.Append(" -WorkingDirectory '").Append(Esc(reg.WorkingDirectory)).Append('\'');
        sb.Append("; ");

        sb.Append("$trigger = ").Append(BuildTrigger(reg.Cadence)).Append("; ");
        sb.Append("Register-ScheduledTask -TaskName '").Append(Esc(reg.Name)).Append('\'');
        sb.Append(" -TaskPath '").Append(Esc(reg.Folder)).Append('\'');
        sb.Append(" -Action $action -Trigger $trigger");
        sb.Append(" -Description 'narnia-job:").Append(Esc(reg.JobId)).Append('\'');
        sb.Append(" -Force");
        return sb.ToString();
    }

    private static string BuildTrigger(ScheduleCadence cadence)
    {
        var at = cadence.TimeOfDay.ToString("HH\\:mm");
        if (cadence.Kind == ScheduleCadenceKind.Weekly && cadence.DaysOfWeek.Count > 0)
        {
            var days = string.Join(",", cadence.DaysOfWeek.Select(d => d.ToString()));
            return $"New-ScheduledTaskTrigger -Weekly -DaysOfWeek {days} -At {at}";
        }

        return $"New-ScheduledTaskTrigger -Daily -At {at}";
    }

    private static string Esc(string value) => value.Replace("'", "''");
}
