---
description: Map live Copilot sessions to Copilot, shell, and Windows Terminal PIDs, then attribute CPU, memory, and child processes without terminating anything.
---

# Live Process Diagnostics

When many Copilot sessions are open, Task Manager can show high CPU or memory without
explaining which session owns the work. Narnia's **🖥️ Processes** page maps live
`copilot.exe` processes back to their Copilot sessions and owning terminal windows.

The page is read-only. It helps identify the expensive process tree before you decide
whether to stop work in the session, close a tab, or end a specific process through
Windows tooling.

## What the page shows

For each live Copilot runtime, Narnia displays:

- The Copilot session summary, repository, branch, and working directory.
- The `copilot.exe` PID.
- The owning shell PID, when a recognized shell is present.
- The owning `WindowsTerminal.exe` PID, when the session runs in Windows Terminal.
- The validated launch chain between the terminal and Copilot runtime.
- CPU, private memory, and working-set memory for the Copilot runtime.
- Aggregated CPU and private memory for the runtime and all child processes.
- An expandable child process tree, including MCP servers and commands launched by Copilot.
- Additional session-state locks sharing the same Copilot runtime PID.

Every Windows Terminal process is also shown, including terminals with no mapped
Copilot session. Terminal totals cover the terminal's complete validated descendant
tree, so they can expose load from another tab or process that is not owned by Copilot.

Use the search box with a Copilot PID, terminal PID, child PID, process name, session
GUID, repository, or working directory. Session detail pages and the **Windows** recovery
page link directly into the corresponding process search.

## Session mapping

Copilot writes an `inuse.<pid>.lock` marker in each active session-state folder. Narnia
starts with verified live `copilot.exe` PIDs and reads those markers to map each runtime
to its session IDs.

A single Copilot runtime can own more than one session-state folder. Additional locks can
represent provisional state created while resuming, a background agent, or an in-process
subtask; a lock alone does not prove another active conversation. Narnia presents the
earliest recorded session as the primary tab session and lists the rest as additional
session locks. Resource usage belongs to the runtime once and is not multiplied by the
number of locks.

Parent-process links are accepted only when the child did not start before the alleged
parent. This guards against Windows PID reuse attaching an unrelated process to an old
terminal or Copilot tree. Traversals also detect cycles and have a depth limit.

## Resource measurements

The page samples cumulative process CPU time while it is open:

- The first sample displays **sampling…** because no CPU delta exists yet.
- Later samples divide processor-time change by elapsed monotonic time and logical
  processor count. The result matches the machine-capacity convention used by Task
  Manager: `100%` means all logical processors are saturated.
- **Sampled process CPU** is an estimate across processes present in consecutive
  samples. A process that starts and exits between samples can be missed.
- Process-tree memory totals use private committed bytes, which can be added without
  multiplying shared DLL pages.
- Per-process working set is also shown, but summed working-set values can count shared
  pages more than once.
- Processes whose start time or complete CPU and memory counters cannot be read are omitted
  rather than displayed as a misleading zero-valued sample.

Terminal and Copilot totals overlap. A Copilot runtime inside Windows Terminal appears
in both totals, so do not add the two percentages together.

The browser refreshes metrics every five seconds only while the page is visible.
Opening or closing a terminal or Copilot runtime reloads the page so ownership remains
correct. Stable parent and nested-session ownership is refreshed periodically without
slowing every resource sample. Persistent child additions and removals are confirmed across
two polls before the static process tree reloads; unrelated short-lived commands do not block
that confirmation. No resource samples are persisted.

## Cleaning up safely

Use the process tree to find the runtime or child consuming resources, then prefer the
least disruptive cleanup:

1. Open the linked session and let an active command finish or cancel it normally.
2. Close only the affected Copilot tab when the runtime itself is no longer useful.
3. If a child process remains stuck, use its exact PID in Task Manager or PowerShell
   after confirming it is not still handling session work.
4. Close an entire Windows Terminal window only when every tab in that terminal can stop.

Narnia does not expose an **End process** action. Abrupt termination can lose in-flight
assistant output, interrupt file operations, or leave child processes behind. A future
termination workflow should require explicit confirmation and distinguish stopping one
child, one Copilot runtime, one tab, and an entire terminal window.

## Platform and privacy

Live diagnostics currently require Windows and WMI. The process sample includes resource
metadata and process image names, but Narnia does not collect or expose process command
lines on this page. The API is available at `GET /api/processes` on the loopback-only
Narnia server.

Diagnostics read Copilot's session-state lock files and session metadata. They do not
modify Copilot's `session-store.db`, and they add no Narnia database tables.
