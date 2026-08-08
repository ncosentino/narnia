# Git Worktrees

Narnia understands Git worktrees so that two Copilot sessions working on one repository can be
kept in genuinely separate working trees instead of quietly sharing one.

## The problem this solves

A session detail page has two override fields that look related but historically were not:

| Field | What it does |
| --- | --- |
| **Preferred Resume Directory** (`local_path`) | **Decides where a launch actually starts.** |
| **Branch** | A display label. It has never selected a branch, and still does not. |

Nothing stopped those two from disagreeing. A session could be labelled with branch
`legacy-label` while its resume directory pointed at the repository's main worktree on a
completely different branch — and `legacy-label` did not have to exist at all. The session list
showed the label, so everything looked correctly separated while both sessions launched into the
same directory.

Narnia now surfaces that divergence and gives you a way to fix it in one action.

## Worktree picker

The override editor has a **Git Worktree** dropdown listing every worktree of the session's
repository, discovered with `git worktree list --porcelain`. Each entry shows its path, the branch
checked out there, and which one the session currently resolves to.

Choosing an entry fills in **both** the resume directory and the branch together, so the two cannot
drift apart. The picker only offers worktrees that exist on disk, and skips bare repositories.

Narnia never runs `git checkout`. The picker points a session at a worktree that already exists; it
does not create one or move a branch. See [Why Narnia will not check out a branch](#why-narnia-will-not-check-out-a-branch).

## Coherence warnings

Warnings only appear when a session has a **branch override**, and only when that override actually
misleads. Two things are deliberately **not** warned about:

- **A session with no branch override.** Working outside version control is an ordinary state —
  Narnia's own scheduled jobs run from the Windows system directory — so a session that has claimed
  no branch has nothing incoherent to report.
- **A real branch that simply is not checked out right now.** Switching branches in place is ordinary
  Git use. A session labelled `main` in a repository currently on a feature branch is normal, and
  warning about it would fire constantly and bury the cases that matter.

When a session's branch override genuinely disagrees with reality, a warning appears above the
override editor:

| Warning | Meaning |
| --- | --- |
| `BranchNotFound` | The branch override names a branch that does not exist in the repository at all, so the label can never be satisfied. |
| `BranchInDifferentWorktree` | The branch is real but lives in another worktree; the session launches somewhere else. Offers a **Use this worktree** button. |
| `NotARepository` | Git ran and reported the launch directory is not inside a repository, so the branch label cannot be matched against anything. |
| `GitUnavailable` | The check did not complete — Git could not be run, timed out, or the directory could not be inspected. |

`BranchInDifferentWorktree` is the actionable one — **Use this worktree** fills the override fields
with the worktree that already holds the branch. Review the values and press **Save**; nothing is
written until you do.

## Shared-directory guard

Before launching, Narnia checks whether any tab would land in a directory that another Copilot
session is already using. If so the launch is refused and you are asked to confirm:

> Two Copilot agents would share one working tree… They can overwrite each other's edits, and a Git
> command run by one reshapes the other's working tree.

Confirming launches anyway. The check covers both cases:

- a tab colliding with an **already-running** session, and
- two tabs in the **same** launch request resolving to one directory.

Relaunching a session into the directory it is already running in is not treated as a collision —
that is a normal reopen, governed by the existing resume-safety checks.

### What the guard can and cannot see

The guard compares the directories Narnia itself would launch into, resolved with the usual
precedence:

1. the `local_path` override,
2. the session store's recorded working directory,
3. the workspace Git root.

It does **not** read a live process's actual working directory. Two consequences follow:

- A session whose override was edited after it was launched is judged by the new value.
- An agent that changed directory after launch is invisible to the guard.

Narnia can guarantee *where a session starts*, not where it goes afterwards.

## Why Narnia will not check out a branch

Making the branch override drive a launch by running `git checkout` would be wrong in three
independent ways:

1. **Git forbids it across worktrees.** A branch can be checked out in only one worktree:

    ```text
    $ git -C C:\dev\repo checkout feature/x
    fatal: 'feature/x' is already used by worktree at 'C:/dev/repo-worktree'
    ```

    The multi-worktree case — the one this feature exists for — would always fail.

2. **It mutates a working tree from a metadata field.** A display label should never rewrite files
   on disk.

3. **It can corrupt a running session.** Checking out in a directory another agent is using pulls
   the tree out from under it mid-task.

So the picker redirects the session to the worktree that already holds the branch, and the
advisories tell you when they disagree.

## Sessions outside a Git repository

Nothing here requires a session to be in a repository. A session working in a plain directory:

- produces **no warning** — there is nothing to be inconsistent with,
- shows a disabled worktree dropdown reading *No Git worktrees found for this session*, and
- is still covered by the shared-directory guard, which never runs Git.

The only case that warns is a session that has a branch override *and* a launch directory that is
not a repository — a label that cannot possibly match anything.

## MCP

The same data is available to agents through
[`get_session_worktrees`](tools/get-session-worktrees.md).
