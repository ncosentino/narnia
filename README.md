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

## Skills (Plugin System)

Narnia ships with agentic skills that can be loaded by Copilot CLI or Claude Code via their plugin systems. Skills let the LLM manage the web UI lifecycle directly — with full visibility into build output and adaptive error handling.

### Available Skills

| Skill | Description |
|-------|-------------|
| `narnia-web-server` | Start, stop, restart, and check status of the Narnia web UI |

### Installing as a Plugin

Narnia follows the standard plugin layout with `plugin.json` at the repo root.

**Copilot CLI:**

```bash
copilot plugin install ncosentino/narnia
```

**Claude Code:**

```bash
claude plugin install ncosentino/narnia
```

**From a local clone:**

```bash
copilot plugin install ./path/to/narnia
```

**Useful commands:**

```bash
copilot plugin list              # List installed plugins
copilot plugin update narnia     # Update to latest
copilot plugin uninstall narnia  # Remove the plugin
```

Once installed, skills are automatically available. Just ask the LLM to perform the task (e.g., "start the Narnia web server").

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
  skills/
    narnia-web-server/            # Agentic skill for web UI lifecycle management
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

---

## Related Projects

- [google-search-console-mcp](https://github.com/ncosentino/google-search-console-mcp) -- Google Search Console MCP server: query clicks, impressions, CTR, and ranking position from your Search Console properties
- [google-keyword-planner-mcp](https://github.com/ncosentino/google-keyword-planner-mcp) -- Google Ads Keyword Planner MCP server: keyword ideas, search volume, competition, and CPC data
- [google-psi-mcp](https://github.com/ncosentino/google-psi-mcp) -- Zero-dependency MCP server for Google PageSpeed Insights Core Web Vitals

---

## About

### Nick Cosentino -- Dev Leader

This tool was built by **[Nick Cosentino](https://www.devleader.ca)**, a software engineer and content creator known as **Dev Leader**. Nick creates practical .NET, C#, ASP.NET Core, Blazor, and software engineering content for intermediate to advanced developers -- covering everything from performance optimization and clean architecture to real-world career advice.

Narnia was born out of a real frustration: Windows forcing a restart and wiping every active Copilot CLI session with no easy way to recover. It serves as a practical example of building NativeAOT C# MCP servers and Blazor Static SSR apps following modern .NET standards.

**Find Nick online:**

- Blog: [https://www.devleader.ca](https://www.devleader.ca)
- YouTube: [https://www.youtube.com/@devleaderca](https://www.youtube.com/@devleaderca)
- Newsletter: [https://weekly.devleader.ca](https://weekly.devleader.ca)
- LinkedIn: [https://linkedin.com/in/nickcosentino](https://linkedin.com/in/nickcosentino)
- All My Links: [https://links.devleader.ca](https://links.devleader.ca)

### BrandGhost

[BrandGhost](https://www.brandghost.ai) is a social media automation platform built by Nick that lets content creators cross-post and schedule content across all social platforms in one click. If you create content and want to spend less time on distribution and more time creating, check it out.

---

## Contributing

Contributions are welcome! Please:

1. Open an issue describing the bug or feature request before submitting a PR
2. Run `dotnet build` with zero warnings before submitting
3. Run `dotnet test` -- all tests must pass

---

## License

MIT License -- see [LICENSE](LICENSE) for details.
