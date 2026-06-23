namespace NexusLabs.Narnia.Core.Configuration;

public sealed class NarniaOptions
{
    public const string SectionName = "Narnia";

    public string DatabasePath { get; set; } = GetDefaultDatabasePath();
    public string SettingsDatabasePath { get; set; } = GetDefaultSettingsDatabasePath();
    public string SessionStatePath { get; set; } = GetDefaultSessionStatePath();

    /// <summary>
    /// Default interval, in seconds, between terminal-window snapshots. Overridable at
    /// runtime via the <c>snapshotter_interval_seconds</c> setting. Clamped to a small
    /// minimum to avoid a busy loop.
    /// </summary>
    public int SnapshotterIntervalSeconds { get; set; } = 60;

    /// <summary>
    /// Default number of most-recent closed windows to retain. Overridable at runtime via
    /// the <c>snapshotter_retention_count</c> setting. Pinned windows are never pruned.
    /// </summary>
    public int SnapshotterRetentionCount { get; set; } = 50;

    /// <summary>
    /// Whether the snapshotter runs by default. Overridable at runtime via the
    /// <c>snapshotter_enabled</c> setting (so it can be stopped/restarted without a server restart).
    /// </summary>
    public bool SnapshotterEnabled { get; set; } = true;

    /// <summary>
    /// When set, used directly as the SQLite connection string instead of building one from <see cref="DatabasePath"/>.
    /// Intended for testing with in-memory SQLite databases.
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// When set, used directly as the SQLite connection string for the Narnia settings database
    /// instead of building one from <see cref="SettingsDatabasePath"/>.
    /// Intended for testing with in-memory SQLite databases.
    /// </summary>
    public string? SettingsConnectionString { get; set; }

    private static string GetDefaultDatabasePath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".copilot", "session-store.db");

    private static string GetDefaultSettingsDatabasePath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".copilot", "narnia-settings.db");

    private static string GetDefaultSessionStatePath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".copilot", "session-state");
}
