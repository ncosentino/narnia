---
description: Configuration snippets for adding Narnia to GitHub Copilot CLI, VS Code, Cursor, Visual Studio, and Claude Desktop. No API key required.
---

# Setup by Tool

Narnia's MCP server is a **local, loopback-only Streamable HTTP endpoint** (`http://127.0.0.1:5244/mcp`) served by the same always-on process as the web UI — it is not a stdio process that your MCP client launches itself. No API key or authentication is required; it only reads from your local `~/.copilot/` files.

!!! warning "Start the server first"
    Every snippet below assumes Narnia is already running (see [Getting Started](getting-started.md)). GitHub Copilot CLI's `sessionStart` hook keeps it running automatically once installed as a plugin; other clients don't know to start it for you, so start it yourself first — otherwise the client will show a connection error or zero tools.

---

## GitHub Copilot CLI

Installing Narnia as a plugin (`copilot plugin install ncosentino/narnia`) ships a working `.mcp.json` in the plugin bundle — nothing to hand-edit:

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

The same plugin's `sessionStart` hook also relaunches the server at the start of every session if it isn't already up.

---

## VS Code

Add to `.vscode/mcp.json` in your workspace (or run **MCP: Add Server** from the Command Palette and choose **HTTP**):

```json
{
  "servers": {
    "narnia": {
      "type": "http",
      "url": "http://127.0.0.1:5244/mcp"
    }
  }
}
```

---

## Cursor

Add to `.cursor/mcp.json` in your project root (or a user-level `~/.cursor/mcp.json` to make it available everywhere):

```json
{
  "mcpServers": {
    "narnia": {
      "url": "http://127.0.0.1:5244/mcp"
    }
  }
}
```

Cursor infers the transport from the presence of `url` (no `command` or `type` needed) — this is its documented format for a remote-style server.

---

## Visual Studio

Add to `%USERPROFILE%\.mcp.json` (available to every solution) or `<SolutionDir>\.mcp.json` (that solution only). Requires Visual Studio 2022 17.14+ or Visual Studio 2026:

```json
{
  "servers": {
    "narnia": {
      "url": "http://127.0.0.1:5244/mcp"
    }
  }
}
```

---

## Claude Desktop

Claude Desktop's own config file (`claude_desktop_config.json`) only launches **stdio** servers directly. Its URL-based "Custom Connectors" feature is a claude.ai (web) integration built for internet-hosted, authenticated servers — it doesn't apply to a loopback-only local server like Narnia's.

The practical workaround is [`mcp-remote`](https://www.npmjs.com/package/mcp-remote), a small stdio-to-HTTP bridge process, referenced from `claude_desktop_config.json` (`~/Library/Application Support/Claude/` on macOS, `%APPDATA%\Claude\` on Windows):

```json
{
  "mcpServers": {
    "narnia": {
      "command": "npx",
      "args": ["mcp-remote", "http://127.0.0.1:5244/mcp"]
    }
  }
}
```

---

## Protocol compatibility

Narnia's MCP server speaks the **2026-07-28** MCP revision and still negotiates down-level with
clients on `2025-11-25`, `2025-06-18`, and `2025-03-26`. Version negotiation is automatic — there
is nothing to configure.

MCP clients periodically adopt newer revisions of the specification. If a client updates to a
revision your installed Narnia predates, the connection fails at the handshake with an error like:

```text
failed to negotiate MCP lifecycle: JSON-RPC error: -32000:
Bad Request: The MCP-Protocol-Version header value '2026-07-28' is not supported.
```

Despite the wording, this is **not** an authentication or network problem — Narnia's endpoint
requires no credentials. It means the client is asking for a newer protocol revision than the
running server understands. Fix it by updating Narnia to the latest release (see
[Getting Started](getting-started.md)), then restarting the server so the new build is listening.

---

See [Configuration](configuration.md) for environment variable overrides to customize the database path and session state directory.
