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
    public static string Build(ScheduledTaskRegistration reg) =>
        reg.Cadence.Kind == ScheduleCadenceKind.Monthly
            ? BuildMonthly(reg)
            : BuildCmdlet(reg);

    // Daily/weekly use New-ScheduledTaskTrigger, which supports those cadences directly.
    private static string BuildCmdlet(ScheduledTaskRegistration reg)
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

    // New-ScheduledTaskTrigger has no monthly option, so a monthly task is registered from a full
    // task XML with a ScheduleByMonth calendar trigger — the same representation Task Scheduler uses.
    private static string BuildMonthly(ScheduledTaskRegistration reg)
    {
        var time = reg.Cadence.TimeOfDay.ToString("HH\\:mm");
        var day = Math.Clamp(reg.Cadence.DayOfMonth, 1, 31);
        const string months =
            "<January/><February/><March/><April/><May/><June/><July/><August/><September/><October/><November/><December/>";
        var workingDir = string.IsNullOrWhiteSpace(reg.WorkingDirectory)
            ? ""
            : $"<WorkingDirectory>{Xml(reg.WorkingDirectory)}</WorkingDirectory>";

        var xml =
            "<?xml version=\"1.0\" encoding=\"UTF-16\"?>" +
            "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">" +
            $"<RegistrationInfo><Description>narnia-job:{Xml(reg.JobId)}</Description></RegistrationInfo>" +
            "<Triggers><CalendarTrigger>" +
            $"<StartBoundary>2024-01-01T{time}:00</StartBoundary><Enabled>true</Enabled>" +
            $"<ScheduleByMonth><DaysOfMonth><Day>{day}</Day></DaysOfMonth><Months>{months}</Months></ScheduleByMonth>" +
            "</CalendarTrigger></Triggers>" +
            "<Principals><Principal id=\"Author\"><LogonType>InteractiveToken</LogonType><RunLevel>LeastPrivilege</RunLevel></Principal></Principals>" +
            "<Settings><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy><StartWhenAvailable>true</StartWhenAvailable><ExecutionTimeLimit>PT0S</ExecutionTimeLimit><Enabled>true</Enabled></Settings>" +
            "<Actions Context=\"Author\"><Exec>" +
            $"<Command>{Xml(reg.Execute)}</Command><Arguments>{Xml(reg.Arguments)}</Arguments>{workingDir}" +
            "</Exec></Actions>" +
            "</Task>";

        var sb = new StringBuilder();
        sb.Append("$xml = '").Append(Esc(xml)).Append("'; ");
        sb.Append("Register-ScheduledTask -TaskName '").Append(Esc(reg.Name)).Append('\'');
        sb.Append(" -TaskPath '").Append(Esc(reg.Folder)).Append('\'');
        sb.Append(" -Xml $xml -Force");
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

    private static string Xml(string value) => value
        .Replace("&", "&amp;")
        .Replace("<", "&lt;")
        .Replace(">", "&gt;")
        .Replace("\"", "&quot;")
        .Replace("'", "&apos;");
}
