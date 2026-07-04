namespace NexusLabs.Narnia.Core.Models;

/// <summary>Session count for one hour of the day (0-23), in the server's local time zone.</summary>
public sealed record HourActivity(int Hour, int SessionCount);
