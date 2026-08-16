---
description: Why the Layout editor supports both Collection-backed windows and individual-session windows on a persisted monitor canvas.
---

# ADR-0005: The Layout editor supports mixed window content

**Status:** Accepted

## Context

[ADR-0004](adr-0004-window-layouts-compose-collections.md) established persisted Layouts whose
captured windows reference Collections. Capture is useful when the desired arrangement already
exists, but it cannot create a Layout from scratch or adjust one after monitor and workflow needs
change.

Some windows represent durable work areas best modeled by Collections. Others contain one
special-purpose session that should not require creating a one-member Collection. Editing through
numeric coordinate forms would expose storage details rather than the visual arrangement users
are trying to create.

## Decision

Layouts persist monitor topology independently from their window slots. A Layout can therefore
exist with no windows and can be created from the current monitor topology without capturing any
open window.

Each window slot contains exactly one of:

- a Collection reference, which launches the Collection's current members as tabs; or
- an individual session reference, which launches only that session.

The Layout editor is a WYSIWYG monitor canvas. Users add Collection or session windows from a
palette, drag windows, resize them, remove them, and save the complete definition atomically.
Coordinates are persisted normalized to the selected monitor work area.

A Collection or individual session can appear only once in a Layout. Launch preflight expands
Collection membership and rejects any session that would therefore appear in more than one
window.

## Alternatives considered

**Require one-member Collections for individual sessions.** Preserves one slot kind but pollutes
the Collection model with containers created only to satisfy Layout mechanics.

**Keep capture as the only authoring workflow.** Avoids an editor but forces users to launch and
manually arrange everything before a Layout can exist.

**Edit numeric rectangles in a form.** Straightforward to implement but does not match the visual
task and makes multi-monitor positioning difficult to understand.

**Persist freeform session lists inside each slot.** Recreates arbitrary grouping inside Layouts
and overlaps Collections. Individual-session slots cover the exception without introducing a
third grouping model.

## Consequences

Migration `0017_add_layout_editor_content.sql` preserves existing Collection slots, adds persisted
monitor topology, and allows individual-session slots.

The editor can create empty Layouts and save empty canvases. A Layout must still contain at least
one monitor because all future placement is relative to that topology.

Dragging across monitors assigns the window to the monitor containing its center when saved.
Windows are clamped to that monitor's normalized work area.

Current monitor topology is captured when creating a blank Layout. Editing monitor topology
itself is outside the initial editor scope.
