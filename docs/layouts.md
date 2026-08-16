---
description: Capture several Collection windows as one monitor-aware Layout and restore the full arrangement with one action.
---

# Window Layouts

A Layout answers a different question from a Collection:

- a **Collection** defines which Copilot sessions belong together;
- a **Layout** defines which Collections open as windows and where those windows belong; and
- **Runtime** reports the windows and processes that exist now.

Layouts live in Narnia's settings database and never modify Copilot-owned session storage.

## Capture the current desktop

Arrange the Windows Terminal windows as desired, then open **Layouts → Capture Current Layout**.
Narnia reads every visible Windows Terminal HWND, including several windows hosted by one shared
`WindowsTerminal.exe` process.

For each captured window:

1. choose the Collection that should occupy it;
2. ignore non-Copilot or unrelated windows; and
3. save the arrangement under a unique Layout name.

Narnia suggests a Collection when the active window title uniquely matches one of its current
members. Suggestions are advisory because a title alone cannot prove the complete tab composition.

## Launch a Layout

**Launch Layout** preflights the complete arrangement before starting anything:

- every referenced Collection must still exist and contain sessions;
- no session may appear in more than one referenced Collection;
- target sessions must not already be active;
- every session must still be indexed; and
- normal working-directory collision checks still apply.

After preflight, Narnia opens each Collection in a new Windows Terminal window, detects the new
HWND, applies saved placement, and verifies the resulting bounds. A partial launch is reported
explicitly; Narnia does not close successfully launched windows to disguise another failure.

Collections remain independently available through **Open Collection** and **Open Selected**.

## Resolution and monitor changes

Every window stores both exact captured pixels and coordinates normalized to its monitor work
area.

- If the monitor and work-area dimensions still match, Narnia restores exact offsets and size.
- If the same monitor has a different resolution or taskbar work area, Narnia scales normalized
  placement.
- If the captured monitor is unavailable, Narnia uses the current primary monitor and clamps the
  window on-screen.

The initial monitor identity is the Win32 display device name. A later version can strengthen
identity with `QueryDisplayConfig` target paths without changing Layout ownership.

## Virtual desktops

The initial restore policy launches onto the virtual desktop that is current when the Layout is
started. Layout storage includes an explicit desktop policy so captured desktop affinity can be
added later through the public virtual-desktop API.

## Native Windows Snap Groups

Windows does not expose a supported public API for assigning arbitrary windows to native Snap
Groups. Narnia restores equivalent visible geometry through supported Win32 placement APIs and
does not claim that the shell recreated native Snap Group membership.
