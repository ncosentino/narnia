---
description: Configuration snippets for adding Narnia to GitHub Copilot CLI, Claude Desktop, Cursor, VS Code, and Visual Studio. No API key required.
---

# Setup by Tool

Narnia runs as a local stdio MCP server. No API key or authentication is required — it reads from your local `~/.copilot/` files only.

Replace the path in each snippet with the actual location of the Narnia binary or project on your machine.

---

## GitHub Copilot CLI

Add to `~/.copilot/mcp-config.json`:

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

Or if using a published binary:

```json
{
  "mcpServers": {
    "narnia": {
      "type": "stdio",
      "command": "C:\\path\\to\\NexusLabs.Narnia.McpServer.exe",
      "args": []
    }
  }
}
```

!!! note
    GitHub Copilot CLI requires `"args": []` when `"type": "stdio"` is specified and no args are needed.

---

## Claude Desktop

Add to `claude_desktop_config.json` (`~/Library/Application Support/Claude/` on macOS, `%APPDATA%\Claude\` on Windows):

```json
{
  "mcpServers": {
    "narnia": {
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "/path/to/narnia/src/NexusLabs.Narnia.McpServer"
      ]
    }
  }
}
```

---

## Cursor

Add to `.cursor/mcp.json` in your project root:

```json
{
  "mcpServers": {
    "narnia": {
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "/path/to/narnia/src/NexusLabs.Narnia.McpServer"
      ]
    }
  }
}
```

---

## VS Code

Add to `.vscode/mcp.json`:

```json
{
  "servers": {
    "narnia": {
      "type": "stdio",
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "/path/to/narnia/src/NexusLabs.Narnia.McpServer"
      ]
    }
  }
}
```

---

## Visual Studio

Add to the MCP configuration in Visual Studio's GitHub Copilot settings:

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

---

See [Configuration](configuration.md) for environment variable overrides to customize the database path and session state directory.
