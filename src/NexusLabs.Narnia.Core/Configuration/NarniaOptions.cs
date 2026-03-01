namespace NexusLabs.Narnia.Core.Configuration;

public sealed class NarniaOptions
{
    public const string SectionName = "Narnia";

    public string DatabasePath { get; set; } = GetDefaultDatabasePath();
    public string SessionStatePath { get; set; } = GetDefaultSessionStatePath();

    /// <summary>
    /// When set, used directly as the SQLite connection string instead of building one from <see cref="DatabasePath"/>.
    /// Intended for testing with in-memory SQLite databases.
    /// </summary>
    public string? ConnectionString { get; set; }

    private static string GetDefaultDatabasePath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".copilot", "session-store.db");

    private static string GetDefaultSessionStatePath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".copilot", "session-state");
}
