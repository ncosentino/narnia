# get_session_worktrees

Lists the Git worktrees a session could launch into, and reports where the session's Narnia branch
override disagrees with real Git state.

Read-only. Narnia runs `git worktree list --porcelain` and nothing else — no branch is ever checked
out. See [Git Worktrees](../worktrees.md) for why.

## Parameters

| Name | Type | Required | Description |
| --- | --- | --- | --- |
| `sessionId` | string | yes | Copilot session ID (GUID). |

## Returns

| Field | Description |
| --- | --- |
| `sessionId` | The session inspected. |
| `resolvedDirectory` | The directory this session would actually launch into today. |
| `resolvedBranch` | The branch that directory currently has checked out. |
| `branchOverride` | The branch label recorded in Narnia's settings database. |
| `worktrees[]` | Every worktree of the repository: `path`, `branch`, `head`, `isBare`, `isDetached`, `isPrimary`, `exists`. |
| `advisories[]` | Disagreements found: `kind`, `message`, `suggestedPath`, `suggestedBranch`. |

Advisory kinds are `BranchNotFound`, `BranchInDifferentWorktree`, `NotARepository`, and
`GitUnavailable`. An empty `advisories` array means either that the overrides are coherent or that
the session has no branch override to check.

Advisories are only produced for a session that has a branch override. A session working outside
version control — a scheduled job, for example — reports `worktrees: []` with no advisories, because
claiming no branch cannot contradict anything.

A branch override that names a **real** branch which simply is not checked out anywhere right now is
**not** reported. Switching branches in place is ordinary Git use, so warning about it would bury the
cases that actually mislead.

`NotARepository` is only reported when Git ran and said so. A timeout, a missing Git executable, or
an unreadable directory is reported as `GitUnavailable` — meaning the check did not complete, not
that the directory is unversioned.

## Example

```json
{
  "sessionId": "f483e90b-d870-4dc8-8d05-84990dc2e428",
  "resolvedDirectory": "C:\\dev\\nexus-labs\\veritas",
  "resolvedBranch": "feature/filesystem-evidence-architecture-496",
  "branchOverride": "worktree-art-a",
  "worktrees": [
    {
      "path": "C:\\dev\\nexus-labs\\veritas",
      "branch": "feature/filesystem-evidence-architecture-496",
      "isPrimary": true,
      "exists": true
    },
    {
      "path": "C:\\dev\\nexus-labs\\artifact0",
      "branch": "feature/png-graded-excavation",
      "isPrimary": false,
      "exists": true
    }
  ],
  "advisories": [
    {
      "kind": "BranchNotFound",
      "message": "The branch override 'worktree-art-a' does not name a branch that exists in this repository, so it is only a label. This session launches into C:\\dev\\nexus-labs\\veritas, which is on 'feature/filesystem-evidence-architecture-496'.",
      "suggestedPath": null,
      "suggestedBranch": null
    }
  ]
}
```

## When to use it

- Auditing whether two sessions that look separated by their branch labels actually share one
  working tree.
- Finding the worktree that holds a branch before pointing a session at it.
- Checking a session's launch directory without opening the Narnia UI.
