# narnia-web-server

Manage the lifecycle of the Narnia server — a single ASP.NET Core process that serves the Blazor web UI at `http://127.0.0.1:5244` **and** the MCP endpoint at `/mcp`, shared by every session and every MCP client.

## Capabilities

| Action | Description |
|--------|-------------|
| **Start** | Check `/health` first (idempotent — never starts a second instance) → publish a stamped build to a run directory → launch it detached → health-check |
| **Stop** | `POST /shutdown` (graceful) → falls back to an identity-checked `Stop-Process` on the recorded PID only if that fails |
| **Restart** | Stop → Start |
| **Update** | Record the running version → Stop → re-publish from the current source → Start → report old → new version |
| **Status** | `GET /health` → up/down; the run-state file adds PID/port/version for a deeper look |

## How It Works

When you ask the LLM to start the Narnia web server, the skill:

1. **Resolves the source** — either an explicit override (a path you supply, or `$NARNIA_ROOT`) or the narnia plugin bundle itself, since this skill ships inside it. There is no `git clone` and no well-known-path search: newer code arrives by updating the plugin, not by cloning here.
2. **Publishes to a run directory** (`<LocalAppData>/narnia/app`) — a frozen `dotnet publish`ed copy, decoupled from the source tree, stamped with a content-derived build identity so `/health` can always tell whether the running server matches the latest source.
3. **Launches it detached** from the run directory — not `dotnet run` from source — so it survives the session ending and the source tree can be rebuilt or updated without touching the running process.
4. **Health-checks** — polls `/health` until the server responds.

Because the server owns a run-state file (`<LocalAppData>/narnia/web-server.json`: PID, port, version, exe path), any session can discover and control a server that a *different* session started, and the skill never accidentally starts a second instance.

## Prerequisites

- [.NET 10+ SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Windows, macOS, or Linux

## Usage Examples

Just ask the LLM naturally:

- *"Start the Narnia web server"*
- *"Is Narnia running?"*
- *"Stop the Narnia web UI"*
- *"Restart Narnia"*
- *"Update and restart Narnia"*

## Configuration

The server listens on **`http://127.0.0.1:5244`** by default, serving the web UI and the MCP endpoint (`/mcp`) from that one port. The database is read from `~/.copilot/session-store.db` by default. See [Configuration](../configuration.md) for override options.

## Keeping it running automatically

Once Narnia is installed as a plugin, a `sessionStart` hook checks `/health` at the start of every Copilot CLI session and relaunches the server if it's down (as long as it has been published at least once) — in practice you rarely need to invoke **Start** by hand after the first time.
