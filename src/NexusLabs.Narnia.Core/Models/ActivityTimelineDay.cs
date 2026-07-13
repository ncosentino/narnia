namespace NexusLabs.Narnia.Core.Models;

/// <summary>
/// Counts recorded session activity attributed to one calendar day.
/// </summary>
/// <param name="Date">The calendar day containing the activity.</param>
/// <param name="SessionCount">Sessions created on the day.</param>
/// <param name="TurnCount">Conversation turns recorded on the day.</param>
/// <param name="FilesTouched">File records first observed on the day.</param>
/// <param name="CheckpointCount">Session checkpoints created on the day.</param>
public sealed record ActivityTimelineDay(
    DateOnly Date,
    int SessionCount,
    int TurnCount,
    int FilesTouched,
    int CheckpointCount);
