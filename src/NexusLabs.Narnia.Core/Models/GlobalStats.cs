namespace NexusLabs.Narnia.Core.Models;

public sealed record GlobalStats(
    int TotalSessions,
    int TotalTurns,
    double AvgTurnsPerSession,
    int TotalFilesTouched,
    string? MostActiveRepository,
    string? BusiestDay);
