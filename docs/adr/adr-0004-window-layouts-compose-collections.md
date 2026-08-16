---
description: Why persisted window Layouts reference Collections while Runtime remains an observation surface.
---

# ADR-0004: Window Layouts compose Collections

**Status:** Accepted

## Context

[ADR-0003](adr-0003-collections-are-the-session-organization-model.md) made Collections the
single user-owned way to group sessions. Users also need to restore several Collection windows
into a saved monitor arrangement with one action, without losing the ability to open any
Collection independently.

Runtime window capture is ephemeral evidence about what exists now. Treating a saved arrangement
as Runtime state would conflate observation with user intent, while storing placement directly on
a Collection would prevent one arrangement from composing several Collections.

Windows does not expose a supported public API for reading or recreating native Snap Group
membership for arbitrary external windows. Supported Win32 APIs do expose window placement,
monitor work areas, and deterministic move/resize operations.

## Decision

Narnia persists **Layouts** as a first-class user-owned model in its settings database.

A Layout contains ordered window slots. Each slot references one Collection and stores:

- captured and work-area-normalized window bounds;
- captured monitor identity and primary-monitor status;
- normal, maximized, or minimized state;
- captured z-order; and
- a virtual-desktop policy.

Collections remain independently launchable. A Layout launch uses each referenced Collection's
current membership rather than copying session IDs into another grouping model.

Runtime supplies read-only capture data. The **Capture current layout** workflow creates a
persisted Layout, but Runtime does not own that Layout.

Restore uses HWND identity, not terminal process ID. Narnia launches each Collection into a new
terminal window, detects the resulting HWND, maps the saved bounds to the current monitor work
area, applies placement through Win32, and verifies the result.

Native Snap Group membership is not a correctness requirement. Narnia restores equivalent visible
geometry and labels snap-like positions as inferred.

## Alternatives considered

**Store placement on Collections.** Simple for one Collection, but cannot express an arrangement
of several Collection windows or multiple alternative arrangements.

**Store session snapshots in Layouts.** Recreates the retired Session Group model and becomes
stale when Collection membership changes.

**Store Layouts under Runtime.** Conflates persisted user intent with transient observation and
makes saved arrangements appear unavailable when nothing is running.

**Require FancyZones or native Windows Snap Groups.** Rejected as a core dependency because
FancyZones exposes no stable per-window placement API and native Snap Group assignment is not a
supported public Windows contract.

## Consequences

Layouts are a separate top-level product concept beside Collections and Runtime. They require an
additive Narnia settings-database migration and never modify Copilot-owned storage.

Resolution and DPI changes are handled by normalized work-area coordinates. When a captured
monitor is unavailable, restore falls back to the current primary monitor and reports the
adaptation instead of placing windows off-screen.

Overlapping Collections can make a Layout invalid because one Copilot session cannot be resumed
into two windows simultaneously. Layout launch must preflight and report those conflicts before
starting any window.

Initial virtual-desktop behavior restores onto the desktop current at launch time. Captured
desktop affinity can be added later through the public virtual-desktop API without changing the
Collection or Layout ownership boundaries.
