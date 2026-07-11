---
description: Step-by-step guide to running Narnia's MCP server and web UI. Get session search working in GitHub Copilot CLI in under five minutes.
---

# Getting Started

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- GitHub Copilot CLI with at least one session in `~/.copilot/session-store.db`

## Step 1: Get Narnia

Clone and build from source — there is no published binary release, so this is currently the only option:

```bash
git clone https://github.com/ncosentino/narnia.git
cd narnia
dotnet build narnia.slnx
```

## Step 2: Start the Server

Narnia is a single process: the web UI and the MCP server are the same thing. Start it from the repo root:

```bash
dotnet run --project src/NexusLabs.Narnia.Web
```

This serves the Blazor web UI at `http://localhost:5244` **and** the MCP endpoint at `http://localhost:5244/mcp`. Leave it running — every step below depends on it.

You can also ask your LLM to do this for you using the [`narnia-web-server` skill](skills/narnia-web-server.md), which publishes a stamped build, launches it detached (so it survives your terminal closing), and health-checks it.

## Step 3: Configure Your MCP Client

Point your MCP client at the running server's `/mcp` endpoint. See [Setup by Tool](setup-by-tool.md) for exact snippets per client.

For **GitHub Copilot CLI**, installing Narnia as a plugin ships a working `.mcp.json` automatically:

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

There's nothing to hand-edit for that path — see [Setup by Tool](setup-by-tool.md#github-copilot-cli).

## Step 4: Verify the Tools Are Available

Start a new Copilot CLI session (or reconnect your MCP client so it picks up the server). Ask it:

> "List my most recent Narnia sessions"

or

> "Find sessions where I was working on the macerus project"

You should see results from your local session history.

## Next Steps

- [Setup by Tool](setup-by-tool.md) — per-client configuration snippets
- [Configuration](configuration.md) — environment variable reference
- [MCP Tools](tools/index.md) — full tool reference
