---
description: Measure Copilot session disk usage, identify stale or growing sessions, and safely delete local session data through the supported SDK.
---

# Session Storage

Narnia's **Storage** page measures the logical disk space used beneath
`~/.copilot/session-state/`. It is separate from the recorded file-activity view:
Storage measures files that belong to Copilot sessions, while **Recorded file activity**
shows project and local paths that Copilot tools touched.

## Storage Scans

Narnia starts a metadata-only scan in the background and repeats it every six hours. Select
**Scan now** to request another scan.

The scanner:

- Reads file sizes and timestamps without reading artifact contents
- Skips filesystem reparse points instead of following them
- Stores cached rollups and daily totals in Narnia's own `settings.db`
- Measures events, session databases, checkpoints, rewind snapshots, artifacts, and other files
- Records incomplete scans and Git/worktree markers as safety signals

Sizes are logical file sizes. Filesystem allocation, compression, and reparse targets can make
actual disk allocation differ.

## Finding Cleanup Candidates

Use the filters to identify sessions by inactivity, minimum size, growth, or storage problems.
Narnia shows an age-versus-size chart, category breakdown, daily growth, and the largest file in
each session.

The default cleanup-candidate view excludes active and protected sessions. Favorites, Narnia
aliases or notes, user-assigned Copilot names, Collection membership, and Session Group membership
protect a session unless you explicitly override those protections.

## Local Session Deletion

Narnia never deletes Copilot files or database rows directly. It revalidates selected sessions and
uses `GitHub.Copilot.SDK` to delete local session data through Copilot's supported interface.

Before deletion, Narnia blocks sessions that:

- Are owned by a live Copilot process
- Are not indexed, have no local state, or have an incomplete storage scan
- Contain a linked Git worktree or filesystem reparse point
- Contain a Git repository with modified files, untracked files, no verifiable upstream, or
  unpushed commits

The UI always performs a dry-run preview before asking for irreversible confirmation.
Narnia records the result of each attempted cleanup in `settings.db` and shows recent outcomes on
the Storage page.

!!! warning "Local data only"
    Deleting local data removes the resumable files on this machine. Synced GitHub copies and
    Narnia-owned aliases, notes, Collections, and Session Group references remain.

## Recorded File Activity

The previous Files experience remains available at the web UI's `/files` route. It searches paths
recorded in Copilot's `session_files` index and is useful for auditing which sessions touched a
path, but it does not represent disk usage.
