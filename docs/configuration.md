---
description: Environment variable reference for Narnia. Configure database path, session state directory, and web UI URL without modifying source code.
---

# Configuration

Narnia reads configuration from environment variables. No API key is required — all data is local to your machine.

## Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `NARNIA__DatabasePath` | `~/.copilot/session-store.db` | Path to the Copilot CLI SQLite session database |
| `NARNIA__SessionStatePath` | `~/.copilot/session-state/` | Directory containing per-session state folders |
| `NARNIA__SettingsDatabasePath` | `<LocalAppData>/narnia/settings.db` | Path to Narnia's own settings database (overrides, settings, recorded terminal windows). On Windows this is `%LOCALAPPDATA%\narnia\settings.db`. Migrated automatically from the legacy `~/.copilot/narnia-settings.db` on first run. |
| `NARNIA__SnapshotterEnabled` | `true` | Default for the [terminal-window snapshotter](terminal-windows.md); overridable at runtime from Settings |
| `NARNIA__SnapshotterIntervalSeconds` | `60` | Default snapshot interval (minimum 5) |
| `NARNIA__SnapshotterRetentionCount` | `50` | Default number of recently-closed windows to retain |

## Setting Variables in mcp-config.json

Pass environment variables via the `env` block in your MCP client config:

```json
{
  "mcpServers": {
    "narnia": {
      "type": "stdio",
      "command": "dotnet",
      "args": ["run", "--project", "/path/to/narnia/src/NexusLabs.Narnia.McpServer"],
      "env": {
        "NARNIA__DatabasePath": "D:\\custom-path\\session-store.db",
        "NARNIA__SessionStatePath": "D:\\custom-path\\session-state\\"
      }
    }
  }
}
```

## Default Paths

The defaults resolve to the standard Copilot CLI locations on all platforms:

- **Windows:** `C:\Users\{username}\.copilot\`
- **macOS / Linux:** `~/.copilot/`

If your Copilot CLI data is in the default location, no configuration is required.

## Web UI Port

The web UI listens on `http://localhost:5244` by default (configured in `src/NexusLabs.Narnia.Web/Properties/launchSettings.json`). To change the port, modify the `applicationUrl` in that file before building.
