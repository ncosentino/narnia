---
description: Troubleshooting common Narnia issues including web server startup failures, missing .NET SDK, incorrect database paths, and session search returning no results.
---

# Troubleshooting

## Web Server Does Not Start

**Symptom:** The web server fails to start or you can't reach `http://localhost:5244`.

**Fix:**

1. Make sure you have the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) installed.
2. Try starting manually: `dotnet run --project src/NexusLabs.Narnia.Web` and check the output for errors.
3. Check if something else is already using port 5244 (`netstat -an | findstr 5244` on Windows).
4. If using the `narnia-web-server` skill, the LLM will show you the build output and can diagnose issues automatically.

---

## Web Server Starts But Can't Connect

**Symptom:** The server process is running but `http://localhost:5244` gives "connection refused."

**Fix:**

1. Wait a few seconds — cold builds can take 30-60+ seconds.
2. Check the server process output for binding errors.
3. If port 5244 is in use, change `applicationUrl` in `src/NexusLabs.Narnia.Web/Properties/launchSettings.json`.

---

## Sessions Not Found by Repository or CWD

**Symptom:** `list_sessions_by_repository` or `list_sessions_by_cwd` returns an empty array.

**Why it happens:** Repository and working-directory tools filter metadata, not conversation keywords. Repository matching uses the effective value after Narnia overrides. Working-directory matching does not convert forward slashes to backslashes.

**Fix:**

- Use `list_recent_sessions` first to see the effective `repository` and recorded `cwd` values.
- Copy the relevant field into the matching tool; do not pass a local path to `list_sessions_by_repository`.
- On Windows, paths are stored with backslashes — use `C:\dev\myproject`, not `C:/dev/myproject`.
- Repository matching is case-insensitive. Working-directory matching is case-insensitive on Windows and case-sensitive on case-sensitive operating systems.

---

## .NET 10 SDK Not Found

**Symptom:** Running `dotnet run --project src/NexusLabs.Narnia.Web` (or `dotnet build narnia.slnx`) fails with "The required .NET version was not found."

**Fix:** Install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0). The Narnia projects target `net10.0` and require the .NET 10 runtime.

---

## MCP Client Shows No Tools / Can't Connect

**Symptom:** Your MCP client (VS Code, Cursor, Visual Studio, Claude Desktop) shows Narnia as errored, disconnected, or with zero tools available.

**Why it happens:** Narnia's MCP server is not a process your client launches itself — it's the `/mcp` endpoint of the already-running Narnia web server. If that server isn't up, there is nothing listening at `http://127.0.0.1:5244/mcp` to connect to.

**Fix:**

1. Check `http://127.0.0.1:5244/health` in a browser (or `curl`) — a connection failure means the server isn't running.
2. Start it: see [Getting Started](getting-started.md), or ask the [`narnia-web-server` skill](skills/narnia-web-server.md) to start it.
3. GitHub Copilot CLI keeps the server running automatically via a `sessionStart` hook once Narnia is installed as a plugin. Other clients (VS Code, Cursor, Visual Studio, Claude Desktop) don't manage this for you — start the server yourself first.
4. Reconnect or reload tools in your MCP client once `/health` returns 200.

---

## MCP Client Reports an Unsupported Protocol Version

**Symptom:** The Narnia web server is running and `/health` returns 200, but your MCP client still fails to connect. GitHub Copilot CLI reports something like:

```text
Failed to start MCP client for narnia: failed to negotiate MCP lifecycle:
JSON-RPC error: -32000: Bad Request:
The MCP-Protocol-Version header value '2026-07-28' is not supported.
```

**Why it happens:** MCP clients periodically adopt newer revisions of the Model Context Protocol. When a client updates to a revision that is newer than the one your installed Narnia build understands, the handshake is rejected before any tool is exposed. The wording resembles an authorization failure, but Narnia's loopback endpoint requires no credentials — this is purely a version mismatch, and the client is the side that moved forward.

**Fix:**

1. Update Narnia to the latest release — see [Getting Started](getting-started.md).
2. Restart the Narnia web server so the new build is the one listening on port 5244.
3. Reconnect in your MCP client. Narnia negotiates down-level automatically, so clients still on older revisions keep working after the upgrade.

---

## Two Sessions Show the Same Directory and Branch

**Symptom:** Two Copilot sessions you set up to work on different worktrees both show the same path and branch in Copilot's header, for example `C:\dev\repo [⎇ feature/x]`, even though Narnia lists them with different branches.

**Why it happens:** Copilot's header shows its own process working directory and the branch that directory currently has checked out — it is reporting reality. Narnia's **Branch** override is only a display label: the launch directory comes from the **Preferred Resume Directory** (`local_path`) override, falling back to the session store's working directory. If both sessions' resume directories point at the same repository, both launch there no matter what the branch labels say. A branch label that names no real branch will never be noticed on its own.

**Fix:**

1. Open the session in Narnia. A **Worktree mismatch** warning appears when the branch override disagrees with Git.
2. If the branch exists in another worktree, press **Use this worktree**; otherwise pick the correct entry from the **Git Worktree** dropdown in the override editor.
3. Press **Save**, then relaunch the session.

Narnia also refuses a launch that would put two agents in one working tree, asking you to confirm first.

See [Git Worktrees](worktrees.md) for details, including why Narnia will not run `git checkout` for you.

---

## A Scheduled Job Reports Success But Did Not Do Its Work

**Symptom:** A scheduled job shows `ok` on the Schedules page and Windows Task Scheduler records `Last Run Result: 0x0`, but the thing the job was supposed to produce never appeared — no database write, no email, no pull request. Its log stops part-way through with no error.

**Why it happens:** The Copilot CLI shuts down gracefully when it is interrupted. It records an `abort` event, writes its usage checkpoint, and exits `0`. The wrapper script passes that exit code straight through, so the scheduler records a successful run. The exit code cannot distinguish a run that finished from one that was cut off.

**Fix:**

1. Look at the job's health on the Schedules page. `interrupted` means Narnia found an abort at the end of that run's Copilot session — the scheduler's success is contradicted by the session itself.
2. Open the run log from the badge. The last thing in it is where the run stopped; anything the prompt asked for after that point did not happen.
3. Finish the interrupted work by hand, or re-run the job with **Run now** if it is safe to repeat.
4. If the health is `ok` but you still suspect a cut-off run, the session may have been cleaned up or the log may name no session — Narnia reports `unknown` in that case and does not guess. Check the log's last line directly.

Narnia can tell you a run was cut short. It cannot tell you what cut it short: the session records the abort, not its origin. Look for an external cause around the timestamp of the last log line.

See [Scheduled Job Health](schedule-health.md) for the full classification and what Narnia deliberately stays quiet about.

---

## Database File Not Found

**Symptom:** All MCP tools return errors mentioning "unable to open database file" or similar SQLite errors.

**Why it happens:** The default database path (`~/.copilot/session-store.db`) does not exist, or Narnia is looking in the wrong location.

**Fix:**

1. Confirm the file exists: on Windows, check `C:\Users\{your-username}\.copilot\session-store.db`.
2. If it's in a different location, set `NARNIA__DatabasePath` to the correct absolute path for the server process (see [Configuration](configuration.md)) — not in a per-client MCP config, since the server is a single always-on process shared by every client.
3. If you've never used Copilot CLI, the database won't exist yet — you need to start at least one Copilot CLI session first.

---

## Search Returns No Results

**Symptom:** `search_sessions` returns an empty array for queries that should match.

**Why it happens:** The FTS5 index may not contain the expected content, or the query syntax is incorrect.

**Fix:**

- Try a simpler query first: a single common word that should appear in your sessions.
- Use prefix matching for partial words: `auth*` instead of `authorization`.
- Use `list_sessions_by_repository` or `list_sessions_by_cwd` for metadata; `search_sessions` only searches indexed content.
- Verify sessions exist with `list_recent_sessions` — if that also returns nothing, the database path is likely wrong.
