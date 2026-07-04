namespace NexusLabs.Narnia.Core.Models;

/// <summary>Session count for one day of the week, in the server's local time zone.</summary>
public sealed record DayOfWeekActivity(DayOfWeek Day, int SessionCount);
