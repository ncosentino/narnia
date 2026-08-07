namespace NexusLabs.Narnia.Core.Configuration;

public sealed class NarniaOptions
{
    public const string SectionName = "Narnia";

    public string DatabasePath { get; set; } = GetDefaultDatabasePath();
    public string SettingsDatabasePath { get; set; } = GetDefaultSettingsDatabasePath();
    public string SessionStatePath { get; set; } = GetDefaultSessionStatePath();

    /// <summary>
    /// Copilot's per-workspace sidebar tab list directory. Each file is named
    /// <c>SHA256(UTF8(cwd))</c> in lowercase hex and records the session identifiers Copilot
    /// restores as sidebar tabs the next time that folder is opened. Narnia only reads this
    /// directory, and only rewrites a file when the user explicitly repairs a workspace.
    /// </summary>
    public string SidebarStatePath { get; set; } = GetDefaultSidebarStatePath();

    /// <summary>
    /// Read-only Copilot plugin installation root used to validate package skill dependencies.
    /// Narnia never writes to this directory.
    /// </summary>
    public string InstalledPluginsPath { get; set; } = GetDefaultInstalledPluginsPath();

    /// <summary>
    /// Directory for Narnia-owned recovery packets created when a Copilot session cannot be
    /// resumed safely.
    /// </summary>
    public string RecoveryDirectory { get; set; } = GetDefaultRecoveryDirectory();

    /// <summary>
    /// Directory that holds the self-contained wrapper scripts and per-run logs for Narnia-owned
    /// scheduled jobs, one subfolder per job. Lives in Narnia's own app-data folder
    /// (<c>%LOCALAPPDATA%\narnia\schedules</c> on Windows) and is never mixed with a user's own scripts.
    /// </summary>
    public string SchedulesDirectory { get; set; } = GetDefaultSchedulesDirectory();

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

    /// <summary>
    /// The current default location for Narnia's own settings database: a dedicated per-app
    /// folder under the platform local-application-data directory
    /// (<c>%LOCALAPPDATA%\narnia\settings.db</c> on Windows, the XDG/Application Support
    /// equivalent elsewhere). This is Narnia's own data and lives in Narnia's own folder —
    /// it is deliberately not placed inside the Copilot-owned <c>~/.copilot</c> directory.
    /// </summary>
    private static string GetDefaultSettingsDatabasePath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "narnia", "settings.db");

    /// <summary>
    /// The pre-1.x location of the settings database: a flat file inside the Copilot-owned
    /// <c>~/.copilot</c> directory. Retained so the settings database can be migrated to
    /// <see cref="GetDefaultSettingsDatabasePath"/> on first run after upgrade.
    /// </summary>
    public static string GetLegacySettingsDatabasePath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".copilot", "narnia-settings.db");

    private static string GetDefaultSessionStatePath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".copilot", "session-state");

    private static string GetDefaultSidebarStatePath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".copilot", "sidebar-sessions-state");

    private static string GetDefaultInstalledPluginsPath() =>        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".copilot", "installed-plugins");

    private static string GetDefaultRecoveryDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "narnia", "recoveries");

    private static string GetDefaultSchedulesDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "narnia", "schedules");
}
