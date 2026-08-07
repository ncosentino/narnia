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

Advisory kinds are `BranchNotCheckedOut`, `BranchInDifferentWorktree`, `NotARepository`, and
`GitUnavailable`. An empty `advisories` array means the overrides are coherent.

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
      "kind": "BranchNotCheckedOut",
      "message": "The branch override 'worktree-art-a' is not checked out in any worktree of this repository, so it is only a label. This session launches into C:\\dev\\nexus-labs\\veritas, which is on 'feature/filesystem-evidence-architecture-496'.",
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
