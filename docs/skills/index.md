# Skills

Narnia ships with **agentic skills** — instructions that let an LLM (such as GitHub Copilot CLI or Claude Code) perform complex tasks on your behalf.

Unlike MCP tools (which run inside the MCP server process), skills run in the LLM's own environment. This gives them full visibility into command output, adaptive error handling, and the ability to manage long-running processes like web servers.

## Available Skills

| Skill | Description |
|-------|-------------|
| [narnia-web-server](narnia-web-server.md) | Start, stop, restart, and check status of the Narnia web UI |
| [narnia-scheduler](narnia-scheduler.md) | Create, migrate, and manage Narnia-owned scheduled Copilot jobs |

## Installing as a Plugin

Narnia follows the standard plugin layout with `plugin.json` at the repo root.

### Option A: Direct Install

**Copilot CLI:**

```bash
copilot plugin install ncosentino/narnia
```

**Claude Code:**

```bash
claude plugin install ncosentino/narnia
```

### Option B: Via Marketplace

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

### From a Local Clone

If you already have the repo checked out:

```bash
copilot plugin install ./path/to/narnia
```

### Managing the Plugin

```bash
copilot plugin list              # List installed plugins
copilot plugin update narnia     # Update to latest version
copilot plugin uninstall narnia  # Remove the plugin
```

Once installed, skills are automatically available to the LLM. Just ask it to perform the task (e.g., "start the Narnia web server").

## Why Skills Instead of MCP Tools?

The original `open_narnia_ui` MCP tool had fundamental reliability issues:

- **Blind fire-and-forget:** The MCP tool launched `dotnet run` but couldn't see build errors or adapt to slow builds
- **Fixed timeout:** A 20-second timeout was often too short for cold builds
- **No diagnostics:** When it failed, neither the LLM nor the user could see what went wrong

Skills solve this by letting the LLM use its native shell tools — it can see build output, retry on failure, and manage processes with full control.
