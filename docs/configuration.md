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
| `NARNIA__SettingsDatabasePath` | `<LocalAppData>/narnia/settings.db` | Path to Narnia's own settings database (overrides, favorites, settings, recorded terminal windows). On Windows this is `%LOCALAPPDATA%\narnia\settings.db`. Migrated automatically from the legacy `~/.copilot/narnia-settings.db` on first run. |
| `NARNIA__SnapshotterEnabled` | `true` | Default for the [terminal-window snapshotter](terminal-windows.md); overridable at runtime from Settings |
| `NARNIA__SnapshotterIntervalSeconds` | `60` | Default snapshot interval (minimum 5) |
| `NARNIA__SnapshotterRetentionCount` | `50` | Default number of recently-closed windows to retain |

## Setting Environment Variables

The web UI and MCP server are the same always-on process — not a per-client stdio process — so these are set once wherever that process runs, not in a per-MCP-client `env` block.

**Development** (`dotnet run` from a clone):

```powershell
$env:NARNIA__DatabasePath = "D:\custom-path\session-store.db"
dotnet run --project src/NexusLabs.Narnia.Web
```

Or set them in `src/NexusLabs.Narnia.Web/appsettings.Development.json` (copy `appsettings.Development.json.example`):

```json
{
  "Narnia": {
    "DatabasePath": "D:/custom-path/session-store.db",
    "SessionStatePath": "D:/custom-path/session-state"
  }
}
```

**Published/detached server:** set the variables in the environment before the process launches — e.g. in the user's persistent environment variables, or ahead of invoking the [`narnia-web-server` skill](skills/narnia-web-server.md) / running the published executable directly.

## Default Paths

The defaults resolve to the standard Copilot CLI locations on all platforms:

- **Windows:** `C:\Users\{username}\.copilot\`
- **macOS / Linux:** `~/.copilot/`

If your Copilot CLI data is in the default location, no configuration is required.

## Web UI + MCP Port

The web UI and the MCP endpoint (`/mcp`) are served from the same port — `http://localhost:5244` by default (configured in `src/NexusLabs.Narnia.Web/Properties/launchSettings.json` for `dotnet run`, or via a `--urls` argument when launching a published build). If you change it, update your MCP client's `url` to match.
