---
description: Step-by-step guide to running Narnia's MCP server and web UI. Get session search working in GitHub Copilot CLI in under five minutes.
---

# Getting Started

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- GitHub Copilot CLI with at least one session in `~/.copilot/session-store.db`

## Step 1: Get Narnia

**Option A — Clone and build from source:**

```bash
git clone https://github.com/ncosentino/narnia.git
cd narnia
dotnet build
```

**Option B — Download the published binary:**

Download the latest release from [GitHub Releases](https://github.com/ncosentino/narnia/releases/latest) for your platform.

## Step 2: Configure Your MCP Client

Add Narnia to your MCP client configuration. See [Setup by Tool](setup-by-tool.md) for per-client snippets.

For **GitHub Copilot CLI**, add to `~/.copilot/mcp-config.json`:

```json
{
  "mcpServers": {
    "narnia": {
      "type": "stdio",
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "C:\\path\\to\\narnia\\src\\NexusLabs.Narnia.McpServer"
      ]
    }
  }
}
```

Replace the path with the actual location of the cloned repo on your machine.

## Step 3: Verify the Tools Are Available

Start a new Copilot CLI session. Ask it:

> "List my most recent Narnia sessions"

or

> "Find sessions where I was working on the macerus project"

You should see results from your local session history.

## Step 4 (Optional): Run the Web UI

The Narnia web UI provides a visual browser for session history. Start it from the repo root:

```bash
dotnet run --project src/NexusLabs.Narnia.Web
```

Then navigate to `http://localhost:5244` in your browser.

You can also ask your MCP client to open it for you via the [`open_narnia_ui`](tools/open-narnia-ui.md) tool.

## Next Steps

- [Setup by Tool](setup-by-tool.md) — per-client configuration snippets
- [Configuration](configuration.md) — environment variable reference
- [MCP Tools](tools/index.md) — full tool reference
