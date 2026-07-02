using NexusLabs.Narnia.Core.Models;
using NexusLabs.Narnia.Core.Services;

namespace NexusLabs.Narnia.Core.Tests;

public sealed class ScheduledTaskRegistrationScriptTests
{
    private static ScheduledTaskRegistration Reg(ScheduleCadence cadence) => new(
        JobId: "abc-123",
        Folder: @"\Narnia\",
        Name: "Sample Daily",
        Execute: "powershell.exe",
        Arguments: "-NoProfile -File 'C:\\s\\run.ps1' -Lookback 24h",
        WorkingDirectory: @"C:\dev\repo",
        Cadence: cadence);

    [Fact]
    public void Build_Daily_EmitsDailyTriggerAndMarker()
    {
        var script = ScheduledTaskRegistrationScript.Build(
            Reg(new ScheduleCadence(ScheduleCadenceKind.Daily, new TimeOnly(5, 0), [])));

        Assert.Contains("New-ScheduledTaskTrigger -Daily -At 05:00", script);
        Assert.Contains("-TaskName 'Sample Daily'", script);
        Assert.Contains(@"-TaskPath '\Narnia\'", script);
        Assert.Contains("-Description 'narnia-job:abc-123'", script);
        Assert.Contains("-WorkingDirectory 'C:\\dev\\repo'", script);
        Assert.Contains("-Force", script);
    }

    [Fact]
    public void Build_Weekly_EmitsDaysOfWeek()
    {
        var script = ScheduledTaskRegistrationScript.Build(
            Reg(new ScheduleCadence(ScheduleCadenceKind.Weekly, new TimeOnly(5, 30), [DayOfWeek.Monday, DayOfWeek.Friday])));

        Assert.Contains("-Weekly -DaysOfWeek Monday,Friday -At 05:30", script);
    }

    [Fact]
    public void Build_EscapesSingleQuotes()
    {
        var reg = Reg(new ScheduleCadence(ScheduleCadenceKind.Daily, new TimeOnly(6, 0), [])) with { Name = "Bob's Job" };
        var script = ScheduledTaskRegistrationScript.Build(reg);
        Assert.Contains("-TaskName 'Bob''s Job'", script);
    }

    [Fact]
    public void Cadence_Describe_IsHumanReadable()
    {
        Assert.Equal("Daily 05:00", new ScheduleCadence(ScheduleCadenceKind.Daily, new TimeOnly(5, 0), []).Describe());
        Assert.Equal("Weekly Mon,Fri 05:30",
            new ScheduleCadence(ScheduleCadenceKind.Weekly, new TimeOnly(5, 30), [DayOfWeek.Monday, DayOfWeek.Friday]).Describe());
    }
}
