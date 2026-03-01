# Narnia

**Narnia** is a hybrid MCP server + Blazor web UI for browsing and searching [GitHub Copilot CLI](https://githubnext.com/projects/copilot-cli) session history.

If you use Copilot CLI heavily, you know the pain: a Windows update reboots your machine and every active session terminal is gone. Narnia makes it easy to find and resume sessions — either by asking a new Copilot session to search for you (via MCP), or by browsing a local web interface.

---

## Features

- **MCP Server** — exposes tools that any MCP-compatible client (including Copilot CLI) can call to search and browse your session history
- **Web UI** — Blazor-based local web interface for browsing, searching, and reading session details, checkpoints, and conversation turns
- **Session workspace** — reads supplemental metadata from `~/.copilot/session-state/` including git root and session artifact files

---

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Copilot CLI with an existing `~/.copilot/session-store.db`

---

## Running the MCP Server

The MCP server uses **stdio transport** and is designed to be launched by an MCP host.

### Build

```bash
dotnet build src/NexusLabs.Narnia.McpServer
```

### Configure in Copilot CLI

Add the following to your `~/.copilot/mcp-config.json`:

```json
{
  "mcpServers": {
    "narnia": {
      "command": "dotnet",
      "args": ["run", "--project", "C:/path/to/narnia/src/NexusLabs.Narnia.McpServer"],
      "env": {}
    }
  }
}
```

After publishing as a NativeAOT binary (`dotnet publish src/NexusLabs.Narnia.McpServer -c Release`):

```json
{
  "mcpServers": {
    "narnia": {
      "command": "C:/path/to/NexusLabs.Narnia.McpServer.exe",
      "args": [],
      "env": {}
    }
  }
}
```

### Available MCP Tools

| Tool | Description |
|------|-------------|
| `list_recent_sessions` | Most recently updated sessions |
| `search_sessions` | FTS5 full-text search across summaries, turns, and checkpoints |
| `get_session_details` | Full session metadata and statistics |
| `get_session_checkpoints` | All checkpoints with structured content |
| `get_session_turns` | Conversation turns (paginated) |
| `get_session_workspace` | Git root and session artifact files from the filesystem |
| `list_sessions_by_repository` | Filter sessions by git repository |
| `list_sessions_by_cwd` | Filter sessions by working directory |

---

## Running the Web UI

```bash
dotnet run --project src/NexusLabs.Narnia.Web
```

Then open [http://localhost:5000](http://localhost:5000) in your browser.

---

## Configuration

Both the MCP server and web UI read configuration from environment variables:

| Variable | Description | Default |
|----------|-------------|---------|
| `NARNIA__DatabasePath` | Path to `session-store.db` | `~/.copilot/session-store.db` |
| `NARNIA__SessionStatePath` | Path to session state directory | `~/.copilot/session-state` |

---

## Building from Source

```bash
git clone https://github.com/nexus-labs/narnia
cd narnia
dotnet build
dotnet test
```

### Publish MCP server as NativeAOT

```bash
dotnet publish src/NexusLabs.Narnia.McpServer -c Release
```

---

## Architecture

```
narnia/
  src/
    NexusLabs.Narnia.Core/        # Shared library — trim-safe, AOT-compatible
    NexusLabs.Narnia.McpServer/   # MCP server — NativeAOT, stdio transport
    NexusLabs.Narnia.Web/         # Blazor Static SSR web interface
  tests/
    NexusLabs.Narnia.Core.Tests/  # xUnit v3 test suite
```

The **Core** library is the only place where data access logic lives. Both the MCP server and Web app are thin entry points that register Core services and expose them to their respective transports.

### Data sources

- `session-store.db` — SQLite with FTS5 full-text search (opened read-only)
- `session-state/{guid}/workspace.yaml` — flat key:value YAML with filesystem metadata (e.g. `git_root`)
- `session-state/{guid}/files/` — session artifact files (plan.md, context files, etc.)
