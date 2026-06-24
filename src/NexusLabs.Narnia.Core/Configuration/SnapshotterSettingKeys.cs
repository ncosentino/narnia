namespace NexusLabs.Narnia.Core.Configuration;

/// <summary>
/// Narnia-settings keys controlling the terminal-window snapshotter at runtime. Values are
/// stored in the Narnia settings database and read each tick, so changes take effect without
/// restarting the server.
/// </summary>
public static class SnapshotterSettingKeys
{
    /// <summary><c>"true"</c>/<c>"false"</c> — whether the snapshotter performs work each tick.</summary>
    public const string Enabled = "snapshotter_enabled";

    /// <summary>Integer seconds between snapshots.</summary>
    public const string IntervalSeconds = "snapshotter_interval_seconds";

    /// <summary>Integer count of most-recent closed windows to retain.</summary>
    public const string RetentionCount = "snapshotter_retention_count";
}
