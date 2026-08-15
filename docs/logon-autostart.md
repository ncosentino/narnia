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

The task launches a small generated VBScript shim rather than the server executable directly. The
shim starts a generated PowerShell launcher hidden, so the console application never flashes a
window at sign-in.

The PowerShell launcher inspects `NexusLabs.Narnia.Web.runtimeconfig.json` each time it runs:

- a self-contained release starts through `NexusLabs.Narnia.Web.exe` and requires no installed
  .NET runtime;
- a framework-dependent plugin/source build starts through the system `dotnet.exe` and the
  published DLL.

Choosing at launch time keeps autostart valid when the application directory switches between the
release and rolling source channels. It also bypasses an invalid app host if an older installation
left incompatible runtime files behind.

The shim waits for the server, which means Task Scheduler reports the task as
**Running** for as long as the server is alive and records the server's exit
code when it stops.

The launcher overwrites `<LocalAppData>\narnia\logs\autostart.log` on each start with the selected
deployment kind and the server's output. A failed start therefore has both a scheduler result code
and a persistent diagnostic log.

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

While the task is running, Task Scheduler commonly reports `267009` (`0x41301`,
`SCHED_S_TASK_RUNNING`) rather than `0`; the live **Running** state is authoritative. After the
server exits, `0` means a clean shutdown. A missing task means autostart is not actually installed
— re-enable it on the Settings page to repair it.

If the task is **Ready** and the last result is nonzero, inspect
`<LocalAppData>\narnia\logs\autostart.log`. Runtime-host failures such as `0x80008096` are process
exit codes, not Task Scheduler registration failures.

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
- Narnia's application directory is deployment-owned. Source/plugin updates replace the complete
  directory from a staged publish rather than overlaying files from another build mode.
