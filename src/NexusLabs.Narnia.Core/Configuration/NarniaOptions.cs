namespace NexusLabs.Narnia.Core.Configuration;

public sealed class NarniaOptions
{
    public const string SectionName = "Narnia";

    public string DatabasePath { get; set; } = GetDefaultDatabasePath();
    public string SettingsDatabasePath { get; set; } = GetDefaultSettingsDatabasePath();
    public string SessionStatePath { get; set; } = GetDefaultSessionStatePath();
    public string WebUiUrl { get; set; } = "http://localhost:5244";

    /// <summary>
    /// Path to the NexusLabs.Narnia.Web project file or its containing directory.
    /// Used by the <c>open_narnia_ui</c> MCP tool to start the web server when it is not already running.
    /// When null, the tool auto-detects the project by walking up from <see cref="AppContext.BaseDirectory"/>.
    /// </summary>
    public string? WebProjectPath { get; set; }

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
