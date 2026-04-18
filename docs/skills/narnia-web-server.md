# narnia-web-server

Manage the lifecycle of the Narnia web UI — a Blazor Server application for browsing your Copilot CLI session history.

## Capabilities

| Action | Description |
|--------|-------------|
| **Start** | Locate source → build → run in background → health-check |
| **Stop** | Find the running process → terminate it |
| **Restart** | Stop → Start |
| **Status** | Check if the web server is running on `http://localhost:5244` |
| **Update** | Pull latest from git → rebuild → restart if running |

## How It Works

When you ask the LLM to start the Narnia web server, the skill:

1. **Locates the source code** — checks for a local checkout first, then falls back to cloning from GitHub (`https://github.com/ncosentino/narnia.git`) into a cache directory
2. **Builds the project** — runs `dotnet build` as a separate step so any errors are immediately visible
3. **Starts the server** — runs `dotnet run --no-build` as a detached background process that survives session shutdown
4. **Health-checks** — polls `http://localhost:5244` until the server responds

## Prerequisites

- [.NET 10+ SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- `git` (only needed if no local checkout exists)

## Usage Examples

Just ask the LLM naturally:

- *"Start the Narnia web server"*
- *"Is Narnia running?"*
- *"Stop the Narnia web UI"*
- *"Restart Narnia"*
- *"Update and restart Narnia"*

## Configuration

The web server listens on **`http://localhost:5244`** by default (configured in `src/NexusLabs.Narnia.Web/Properties/launchSettings.json`).

The database is read from `~/.copilot/session-store.db` by default. See [Configuration](../configuration.md) for override options.
