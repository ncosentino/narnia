# Narnia

[![CI](https://github.com/ncosentino/narnia/actions/workflows/ci.yml/badge.svg)](https://github.com/ncosentino/narnia/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/ncosentino/narnia?include_prereleases&style=flat-square)](https://github.com/ncosentino/narnia/releases)

**Narnia** is a single ASP.NET Core app — both an MCP server and a Blazor web UI — for browsing and searching [GitHub Copilot CLI](https://githubnext.com/projects/copilot-cli) session history, recovering lost terminal windows, and managing scheduled Copilot jobs.

If you use Copilot CLI heavily, you know the pain: a Windows update reboots your machine and every active session terminal is gone. Narnia makes it easy to find and resume sessions — either by asking a new Copilot session to search for you (via MCP), or by browsing a local web interface.

---

## Features

- **MCP Server** — one shared HTTP endpoint (`/mcp`) exposing 16 tools that any MCP-compatible client (including Copilot CLI) can call to search session history and manage scheduled jobs — no per-client process to launch, every client talks to the same running instance
- **Web UI** — Blazor Static SSR local web interface for browsing, searching, and reading session details, checkpoints, and conversation turns
- **Scheduled Jobs** — create, edit, and monitor Windows Task Scheduler-backed `copilot -p` jobs (daily/weekly/monthly) with hidden/headless execution and live log streaming, from the web UI or MCP
- **Terminal window recovery** — continuously records your open Windows Terminal windows of Copilot tabs so you can reopen a whole multi-tab window after it is closed or lost, like restoring a browser window
- **Session workspace** — reads supplemental metadata from `~/.copilot/session-state/` including git root and session artifact files

---

## Prerequisites

- Windows x64 for the supported prebuilt release and the complete recovery/scheduling feature set
- Copilot CLI with an existing `~/.copilot/session-store.db`
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) only when building from source or using the rolling plugin/source-build channel

---

## Running Narnia

Narnia is a single process: starting the web app also starts the MCP server. There is no
separate MCP server to build or launch.

### Install a tagged Windows x64 release

Download `narnia-win-x64.zip` and `SHA256SUMS.txt` from the
[Releases page](https://github.com/ncosentino/narnia/releases), verify the archive, and extract it
directly into Narnia's application directory:

```powershell
$expected = ((Get-Content .\SHA256SUMS.txt) -split '\s+')[0]
$actual = (Get-FileHash .\narnia-win-x64.zip -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actual -ne $expected) { throw "Narnia release checksum mismatch." }

$runDir = Join-Path $env:LOCALAPPDATA 'narnia\app'
New-Item -ItemType Directory -Path $runDir -Force | Out-Null
Expand-Archive .\narnia-win-x64.zip -DestinationPath $runDir -Force
Start-Process (Join-Path $runDir 'NexusLabs.Narnia.Web.exe') `
  -ArgumentList '--urls','http://127.0.0.1:5244' `
  -WorkingDirectory $runDir `
  -WindowStyle Hidden
```

The release is self-contained and does not require .NET to be installed. Release executables are
currently unsigned, so Windows SmartScreen may identify the publisher as unknown; verify the published
SHA-256 checksum before running the archive.

To update a downloaded release, call `POST http://127.0.0.1:5244/shutdown`, wait for the server to
exit, replace the application directory with the new ZIP contents, and start it again. Settings,
schedules, and window history live outside the application directory.

### Run from source

```bash
dotnet run --project src/NexusLabs.Narnia.Web
```

Then open [http://localhost:5244](http://localhost:5244) in your browser.

You can also ask an LLM to do this for you via the [`narnia-web-server` skill](skills/narnia-web-server/SKILL.md) — the rolling plugin channel resolves the plugin's current source, publishes a stamped development build, launches it detached, and health-checks it. Once Narnia is installed as a plugin, a `sessionStart` hook (`hooks.json`) relaunches an existing published server automatically at the start of every Copilot CLI session.

### Configure your MCP client

The MCP server uses **Streamable HTTP transport** (not stdio), served at `/mcp` on the running instance. Point any MCP-compatible client at it:

```json
{
  "mcpServers": {
    "narnia": {
      "type": "http",
      "url": "http://127.0.0.1:5244/mcp",
      "tools": ["*"]
    }
  }
}
```

This is exactly what this repo's own [`.mcp.json`](.mcp.json) contains, so a Copilot CLI plugin install picks it up with no manual configuration. Because one server instance backs every client, they all see the same session data and scheduled jobs — there's no per-client `env` block or launch command to configure, just the URL.

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
| `list_schedules` | All cataloged scheduled jobs joined to live task status |
| `get_schedule` | A single scheduled job's full catalog entry by id |
| `get_schedule_log` | Read the latest run log and whether the job is still running |
| `create_schedule` | Create a scheduled job and (by default) register its task |
| `update_schedule` | Replace a scheduled job's definition and re-register it |
| `set_schedule_enabled` | Enable/disable a scheduled job's task |
| `run_schedule_now` | Start a scheduled job's task immediately |
| `delete_schedule` | Remove a scheduled job's task, wrapper, and catalog entry |

---

## Skills (Plugin System)

Narnia ships with agentic skills that can be loaded by Copilot CLI or Claude Code via their plugin systems. Skills let the LLM manage the web UI (and its shared MCP endpoint) lifecycle directly — with full visibility into build output and adaptive error handling.

### Available Skills

| Skill | Description |
|-------|-------------|
| `narnia-web-server` | Start, stop, restart, and check status of the Narnia web UI |
| `narnia-scheduler` | Create, migrate, and manage Narnia-owned scheduled Copilot jobs |

### Installing as a Plugin

Narnia follows the standard plugin layout with `plugin.json` at the repo root.

#### Option A: Direct Install

**Copilot CLI:**

```bash
copilot plugin install ncosentino/narnia
```

**Claude Code:**

```bash
claude plugin install ncosentino/narnia
```

#### Option B: Via Marketplace

Register Narnia as a marketplace first, then install from it. This is useful if you want to browse available plugins before installing.

**Copilot CLI** (from inside a session):

```
/plugin marketplace add ncosentino/narnia
/plugin install narnia@narnia
```

**Claude Code:**

```
/plugin marketplace add ncosentino/narnia
/plugin install narnia@narnia
```

#### From a Local Clone

```bash
copilot plugin install ./path/to/narnia
```

#### Managing the Plugin

```bash
copilot plugin list              # List installed plugins
copilot plugin update narnia     # Update to latest
copilot plugin uninstall narnia  # Remove the plugin
```

Once installed, skills are automatically available. Just ask the LLM to perform the task (e.g., "start the Narnia web server").

---

## Configuration

Narnia reads configuration from environment variables (or `appsettings.Development.json` for local development — see `appsettings.Development.json.example`), set once on the machine running the server. Since the web UI and MCP server are the same process, there is a single place to configure them — not a per-MCP-client `env` block.

| Variable | Description | Default |
|----------|-------------|---------|
| `NARNIA__DatabasePath` | Path to `session-store.db` | `~/.copilot/session-store.db` |
| `NARNIA__SessionStatePath` | Path to session state directory | `~/.copilot/session-state` |
| `NARNIA__SettingsDatabasePath` | Path to Narnia's own settings database (overrides, schedules, recorded terminal windows) | `<LocalAppData>/narnia/settings.db` |
| `NARNIA__SnapshotterEnabled` | Whether the terminal-window snapshotter runs by default | `true` |
| `NARNIA__SnapshotterIntervalSeconds` | Snapshot interval in seconds (minimum 5) | `60` |
| `NARNIA__SnapshotterRetentionCount` | Number of recently-closed windows to retain | `50` |

---

## Building from Source

```bash
git clone https://github.com/ncosentino/narnia.git
cd narnia
dotnet build narnia.slnx
dotnet test narnia.slnx
```

### Publishing the Web UI

The web app is a standard ASP.NET Core Blazor Static SSR app — not NativeAOT (Blazor SSR doesn't yet support NativeAOT or full trimming as of .NET 10):

```bash
dotnet publish src/NexusLabs.Narnia.Web -c Release -o <output-dir>
```

The [`narnia-web-server` skill](skills/narnia-web-server/SKILL.md) does this automatically, stamping a content-derived build identity so `/health` always reflects whether the running server matches the latest source.

### Building the Windows release package

```powershell
pwsh -File .\scripts\Publish-NarniaRelease.ps1 `
  -Version "0.1.0-beta.1" `
  -OutputDirectory .\artifacts\release
```

This creates and smoke-tests the self-contained `narnia-win-x64.zip`, then writes
`SHA256SUMS.txt`. Tagged releases run this same script in GitHub Actions. Packaging requires
PowerShell 7; installing and running the resulting release does not.

---

## Architecture

```
narnia/
  skills/
    narnia-web-server/            # Agentic skill for web UI + MCP server lifecycle management
    narnia-scheduler/             # Agentic skill for scheduled Copilot job create/migrate/verify
  src/
    NexusLabs.Narnia.Core/        # Shared library — trim-safe, AOT-compatible
    NexusLabs.Narnia.Web/         # Blazor Static SSR web app + HTTP MCP server (/mcp), one process
  tests/
    NexusLabs.Narnia.Core.Tests/  # xUnit v3 test suite
    NexusLabs.Narnia.Web.Tests/   # WebApplicationFactory integration tests
```

The **Core** library is the only place where data access logic lives. **Web** is the single entry point: it registers Core services, serves the Blazor UI, and hosts the MCP server (`ModelContextProtocol.AspNetCore`) over Streamable HTTP at `/mcp` — all in the same process. There is no separate MCP server project anymore: an earlier NativeAOT stdio `NexusLabs.Narnia.McpServer` project was replaced by this in-process HTTP server so every MCP client shares one always-on instance instead of each spawning its own.

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

Narnia was born out of a real frustration: Windows forcing a restart and wiping every active Copilot CLI session with no easy way to recover. It serves as a practical example of hosting an MCP server directly inside an ASP.NET Core Blazor Static SSR app, following modern .NET standards.

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
