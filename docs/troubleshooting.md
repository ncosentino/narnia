---
description: Troubleshooting common Narnia issues including web server startup failures, missing .NET SDK, incorrect database paths, and session search returning no results.
---

# Troubleshooting

## Web Server Does Not Start from `open_narnia_ui`

**Symptom:** The `open_narnia_ui` tool returns an error like "Could not locate the Narnia web project."

**Why it happens:** When running via `dotnet run`, the tool walks up from the MCP server binary directory to find `src/NexusLabs.Narnia.Web/NexusLabs.Narnia.Web.csproj`. If you've installed the MCP server binary somewhere other than the repo root, auto-detection fails.

**Fix:** Set `NARNIA__WebProjectPath` explicitly in your MCP client config:

```json
"env": {
  "NARNIA__WebProjectPath": "C:\\path\\to\\narnia\\src\\NexusLabs.Narnia.Web"
}
```

Or point it at the directory (the tool will look for `NexusLabs.Narnia.Web.csproj` inside it).

---

## Web Server Starts But Browser Doesn't Open

**Symptom:** The tool says "Started the web server but it did not become reachable at http://localhost:5244 within 20 seconds."

**Why it happens:** Either the server took longer than 20 seconds to start, or the port is blocked. The web server might still be starting in the background.

**Fix:**

1. Wait a few seconds and navigate to `http://localhost:5244` manually.
2. Check if something else is already using port 5244 (`netstat -an | findstr 5244` on Windows).
3. Change the port by setting `NARNIA__WebUiUrl` to a different URL in both the MCP config and the web app's `appsettings.json`.

---

## Sessions Not Found by Repository or CWD

**Symptom:** `list_sessions_by_repository` or `list_sessions_by_cwd` returns an empty array.

**Why it happens:** Path and repository matching is exact. The stored `cwd` must match exactly — including casing and whether it uses forward or backward slashes.

**Fix:**

- Use `list_recent_sessions` first to see actual `cwd` and `repository` values stored in the database.
- Copy the exact value from the results into your next query.
- On Windows, paths are stored with backslashes — use `C:\dev\myproject`, not `C:/dev/myproject`.

---

## .NET 10 SDK Not Found

**Symptom:** Running `dotnet run --project src/NexusLabs.Narnia.McpServer` fails with "The required .NET version was not found."

**Fix:** Install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0). The Narnia projects target `net10.0` and require the .NET 10 runtime.

---

## Database File Not Found

**Symptom:** All MCP tools return errors mentioning "unable to open database file" or similar SQLite errors.

**Why it happens:** The default database path (`~/.copilot/session-store.db`) does not exist, or Narnia is looking in the wrong location.

**Fix:**

1. Confirm the file exists: on Windows, check `C:\Users\{your-username}\.copilot\session-store.db`.
2. If it's in a different location, set `NARNIA__DatabasePath` to the correct absolute path in your MCP config.
3. If you've never used Copilot CLI, the database won't exist yet — you need to start at least one Copilot CLI session first.

---

## Search Returns No Results

**Symptom:** `search_sessions` returns an empty array for queries that should match.

**Why it happens:** The FTS5 index may not contain the expected content, or the query syntax is incorrect.

**Fix:**

- Try a simpler query first: a single common word that should appear in your sessions.
- Use prefix matching for partial words: `auth*` instead of `authorization`.
- Verify sessions exist with `list_recent_sessions` — if that also returns nothing, the database path is likely wrong.
