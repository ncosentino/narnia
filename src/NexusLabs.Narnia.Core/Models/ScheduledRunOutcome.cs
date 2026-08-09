namespace NexusLabs.Narnia.Core.Models;

/// <summary>
/// How a scheduled job's most recent Copilot run ended, independent of the exit code the OS
/// scheduler recorded.
/// </summary>
public enum ScheduledRunCompletion
{
    /// <summary>The run's ending could not be determined from the log or the session event stream.</summary>
    Unknown,

    /// <summary>The agent finished its work and the session shut down on its own.</summary>
    Completed,

    /// <summary>The agent was interrupted before it finished, so the run's remaining work never happened.</summary>
    Interrupted,
}

/// <summary>
/// The outcome of a scheduled job's most recent run, recovered from the run log and the Copilot
/// session it started.
/// </summary>
/// <remarks>
/// The Copilot CLI shuts down gracefully when it is interrupted, so an interrupted run still exits
/// with code 0 and the OS scheduler still records success. Without this, a job that was killed
/// part-way through is indistinguishable from one that did all of its work.
/// </remarks>
/// <param name="Completion">How the run ended.</param>
/// <param name="SessionId">The Copilot session the run started, when the log names one.</param>
/// <param name="AbortReason">The abort reason the session recorded, when the run was interrupted.</param>
public sealed record ScheduledRunOutcome(
    ScheduledRunCompletion Completion,
    string? SessionId,
    string? AbortReason)
{
    /// <summary>An outcome that asserts nothing, used whenever the run cannot be inspected.</summary>
    public static ScheduledRunOutcome Indeterminate { get; } =
        new(ScheduledRunCompletion.Unknown, null, null);

    /// <summary>Gets whether the run is known to have been interrupted before finishing.</summary>
    public bool WasInterrupted => Completion == ScheduledRunCompletion.Interrupted;
}
