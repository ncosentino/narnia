---
description: Start the Narnia Blazor web UI and open it in your default browser. Automatically launches the web server if it is not already running.
---

# open_narnia_ui

Ensures the Narnia web UI is running and opens it in your default browser. If the server is already running, it opens the browser directly. If not, it starts the server first (using the published binary or `dotnet run`), waits up to 20 seconds for it to become reachable, then opens the browser.

## Parameters

None.

## Response

Returns a plain-text status message:

- `"Narnia web UI was already running. Opened http://localhost:5244 in your default browser."`
- `"Started the Narnia web UI and opened http://localhost:5244 in your default browser."`
- An error message if the web server could not be started (e.g., project path not found)

## How It Works

1. HTTP health check on `NARNIA__WebUiUrl` (default `http://localhost:5244`) with a 2-second timeout
2. If not running — check for `NexusLabs.Narnia.Web[.exe]` next to the MCP binary (published scenario)
3. If not found — resolve the `NexusLabs.Narnia.Web.csproj` by walking up from the MCP binary directory, or use `NARNIA__WebProjectPath` if set
4. Start the process with `UseShellExecute = true`
5. Poll the URL every second for up to 20 seconds
6. Open the browser with `Process.Start`

## Example Prompts

- "Open the Narnia UI"
- "Launch the Narnia web interface"
- "Show me my session browser"
- "Start Narnia and open it in my browser"

## Troubleshooting

If the tool returns an error about not being able to locate the project, set `NARNIA__WebProjectPath` in your MCP config:

```json
"env": {
  "NARNIA__WebProjectPath": "C:\\path\\to\\narnia\\src\\NexusLabs.Narnia.Web"
}
```

See [Troubleshooting](../troubleshooting.md) for more help with web server startup issues.
