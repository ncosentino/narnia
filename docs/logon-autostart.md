---
description: Narnia starts its local server when you sign in to Windows using a Task Scheduler logon task, so a failed autostart is visible instead of silent.
---

# Logon Autostart

Narnia can start its local server automatically when you sign in to Windows, so
terminal-window recording, scheduled jobs, and the MCP endpoint are available
before you open your first Copilot session.

Enable it on the **⚙️ Settings** page under *Start at Login*. It is Windows-only,
off by default, and requires no administrator rights.

## How it works

Enabling autostart registers a per-user **Task Scheduler** entry:

| Property | Value |
|----------|-------|
| Task | `\Narnia\System\Narnia Server Autostart` |
| Trigger | At log on, for your account only, with a 15-second delay |
| Action | `wscript.exe //B //Nologo "<LocalAppData>\narnia\start-server-hidden.vbs"` |
| Runs as | Your interactive session, least privilege |
| If already running | Do not start a second instance |
| Execution time limit | None |
| On failure | Retry up to 3 times, one minute apart |

The task launches a small generated VBScript shim rather than the server
executable directly. The server is a console application, so launching it
without the shim would flash a console window at every sign-in.

The shim waits for the server, which means Task Scheduler reports the task as
**Running** for as long as the server is alive and records the server's exit
code when it stops.

!!! info "Why the task lives in `\Narnia\System\`"
    The **⏱️ Schedules** page lists `\Narnia\` and reports any task without a
    matching catalog entry as an orphaned task. Narnia's own server task is kept
    in a separate subfolder so it is never reported as stray scheduled work.

## Verifying it worked

The Settings page shows the task's registration state, last run time, and last
result once autostart is enabled. The same values are available from the API:

```powershell
Invoke-RestMethod http://127.0.0.1:5244/api/autostart
```

You can also inspect it directly:

```powershell
Get-ScheduledTask -TaskPath '\Narnia\System\' -TaskName 'Narnia Server Autostart' |
    Get-ScheduledTaskInfo
```

A `LastTaskResult` of `0` with state `Running` means the server was launched and
is alive. A missing task means autostart is not actually installed — re-enable it
on the Settings page to repair it.

## Migrating from the `Run` registry entry

Earlier versions started the server from a per-user
`HKCU\Software\Microsoft\Windows\CurrentVersion\Run` value. That mechanism was
silent by design: `wscript.exe //B` suppresses all errors, and Windows does not
log whether a `Run` entry launched successfully. A sign-in where the server never
started looked identical to one where it did, and the only visible symptom was
that `http://127.0.0.1:5244` stayed unreachable until a Copilot session started
the server through its `sessionStart` hook.

Narnia migrates automatically:

- A surviving `Run` value still counts as "autostart enabled", so upgrading never
  silently turns the feature off.
- The next time the server starts, it registers the scheduled task and removes the
  legacy `Run` value.
- The legacy value is only removed **after** the task is registered, so a failed
  migration leaves the old mechanism in place rather than removing both.

Disabling autostart removes the scheduled task, the generated launcher script, and
any remaining legacy `Run` value.

## Behavior notes

- The server runs in your interactive session, so Narnia can still open Windows
  Terminal windows on your desktop. Running it as a background session task would
  break terminal launching.
- Because the task is bound to your logon session, signing out stops the server.
  The Copilot `sessionStart` hook remains the safety net and restarts it whenever
  a session begins and the server is not listening.
- The 15-second logon delay keeps the server from competing with the rest of
  sign-in. It is not throttled by Windows' startup-app queue, unlike a `Run` entry.
