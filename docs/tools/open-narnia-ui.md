---
description: The open_narnia_ui MCP tool has been replaced by the narnia-web-server skill.
---

# open_narnia_ui (Deprecated)

!!! warning "This tool has been removed"
    The `open_narnia_ui` MCP tool has been replaced by the **[narnia-web-server skill](../skills/narnia-web-server.md)**.

The MCP tool had reliability issues — it launched `dotnet run` as a fire-and-forget process with a 20-second timeout and no visibility into build errors. The skill replacement uses the LLM's own shell tools for full lifecycle management with adaptive error handling.

## Migration

Instead of calling the `open_narnia_ui` MCP tool, ask the LLM to use the `narnia-web-server` skill:

- *"Start the Narnia web server"*
- *"Open Narnia"*
- *"Is Narnia running?"*

See the [narnia-web-server skill documentation](../skills/narnia-web-server.md) for details.
