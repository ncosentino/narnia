using NexusLabs.Narnia.Core.Models;

namespace NexusLabs.Narnia.Core.Services;

/// <summary>The outcome of a registrar operation.</summary>
/// <param name="Ok">Whether the operation succeeded.</param>
/// <param name="Error">A human-readable error when it did not.</param>
public sealed record ScheduledTaskCommandResult(bool Ok, string? Error)
{
    public static ScheduledTaskCommandResult Success { get; } = new(true, null);
    public static ScheduledTaskCommandResult Fail(string error) => new(false, error);
}

/// <summary>
/// Writes to the OS scheduler so Narnia can register a standardized task ("do it for me"),
/// adopt-then-convert, and offer convenience controls. Narnia is still not a scheduler — these only
/// register/enable/run/delete tasks the OS owns. Unsupported platforms report it and no-op.
/// </summary>
public interface IScheduledTaskRegistrar
{
    /// <summary>Whether tasks can be written on the current platform.</summary>
    bool IsSupported { get; }

    /// <summary>Registers (or overwrites) the standardized task described by <paramref name="reg"/>.</summary>
    ValueTask<ScheduledTaskCommandResult> RegisterAsync(ScheduledTaskRegistration reg, CancellationToken ct = default);

    /// <summary>Enables or disables a task identified by folder and name.</summary>
    ValueTask<ScheduledTaskCommandResult> SetEnabledAsync(string folder, string name, bool enabled, CancellationToken ct = default);

    /// <summary>Starts a task immediately.</summary>
    ValueTask<ScheduledTaskCommandResult> RunAsync(string folder, string name, CancellationToken ct = default);

    /// <summary>Deletes a task from the scheduler.</summary>
    ValueTask<ScheduledTaskCommandResult> DeleteAsync(string folder, string name, CancellationToken ct = default);
}
