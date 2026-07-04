namespace NexusLabs.Narnia.Core.Models;

public sealed record ActivityPatterns(
    IReadOnlyList<HourActivity> ByHour,
    IReadOnlyList<DayOfWeekActivity> ByDayOfWeek,
    int CurrentStreakDays,
    int LongestStreakDays);
