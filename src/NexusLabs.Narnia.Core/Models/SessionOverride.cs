namespace NexusLabs.Narnia.Core.Models;

public sealed record SessionOverride(
    string SessionId,
    string? DisplayName,
    string? Repository,
    string? Branch,
    string? Notes,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public bool IsArchived { get; init; } = false;

    /// <summary>
    /// Gets whether the session is marked as a favorite.
    /// </summary>
    public bool IsFavorite { get; init; }

    /// <summary>
    /// Optional local filesystem path for resuming sessions.
    /// Shown as the "Preferred Path" resume command and used by the launch button.
    /// </summary>
    public string? LocalPath { get; init; }

    /// <summary>
    /// Custom terminal window title used by the Launch button.
    /// Defaults to the session summary if not set.
    /// </summary>
    public string? TerminalTitle { get; init; }
}
