---
name: review-changes
description: >
  Review the current Narnia diff before commit, push, or pull-request delivery
  against applicable instructions, project docs and ADRs, repository-declared
  validation, and existing CI evidence. Use for review requests, code review,
  PR review, or delivery-readiness checks.
---

# Review Narnia changes

This skill owns review procedure, not project standards. Current instructions, docs, ADRs,
manifests, tests, and workflows remain authoritative.

This is a contributor skill and deliberately lives under `.github/skills/`. The root `skills/`
directory is the product surface published to users by `plugin.json`; nothing here ships to
Narnia's users.

## Review boundary

- Judge changed lines and their direct invariant blast radius.
- Report pre-existing divergence separately and never include it in the verdict.
- Do not demand unrelated migration work because docs describe a target state.
- Do not invent findings for a clean diff.
- Review is read-only unless the user explicitly asks for fixes.

## 1. Resolve the scope

```powershell
git rev-parse --show-toplevel
git branch --show-current
git status --short
```

Use scope in this order:

1. Explicit refs, pull request, or paths supplied by the user.
2. All uncommitted changes: unstaged, staged, and untracked.
3. Otherwise `git merge-base main HEAD` through `HEAD`.

Use `git --no-pager diff`, `git --no-pager diff --cached`, and full reads for untracked files.

Narnia's Git remote is not necessarily named `origin`. Resolve it with `git remote` rather than
assuming, and use `gh pr view` / `gh pr diff` to confirm the actual base and head for a pull
request.

State the selected scope and changed files.

## 2. Resolve governing sources

Resolve applicable instructions for the changed paths:

```powershell
pwsh -NoProfile -Command "& './scripts/guidance/Get-ApplicableInstructions.ps1' -Path 'path/one','path/two'"
```

Read every returned instruction in full. Instructions live in `.github/instructions/`; there is
no generated or externally managed instruction subtree in this repository, so every instruction
is project-owned and editable through normal review.

Then consult, as relevant to the diff:

- `docs/index.md` — the documentation map, whose navigation order is defined in `mkdocs.yml`.
- `docs/adr/index.md` — accepted architecture decisions. Accepted ADRs are immutable; a change
  that contradicts one is either a defect or requires a superseding ADR.
- `docs/design/data-storage.md` — database ownership boundaries.
- `docs/design/delivery.md` — pull request, validation-scope, and public-repository rules.

Follow relevant links from changed docs and matching instructions.

## 3. Resolve validation

```powershell
pwsh -NoProfile -File scripts/guidance/Get-ValidationInventory.ps1
```

Inspect the returned solution, projects, workflows, and delivery metadata before choosing
commands. Narnia's declared surfaces:

| Change touches | Smallest offline command |
|---|---|
| Any C# in `src/` or `tests/` | `dotnet test narnia.slnx --nologo -v quiet` |
| One test project only | add `--project tests/<project>` or `--filter` |
| `docs/**` or `mkdocs.yml` | `python -m mkdocs build --strict` |
| `skills/narnia-report-email/**` | `./skills/narnia-report-email/tests/Send-NarniaReportEmail.Tests.ps1` |
| Guidance surfaces (`AGENTS.md`, instructions, ADRs, docs map) | covered by `dotnet test narnia.slnx` |

Rules:

- Run only the smallest command that covers the changed behavior.
- Do not invent a command the repository does not declare.
- Do not run the release packaging script or hosted/credentialed scenarios on a workstation;
  `release-package` and `docs` jobs in `.github/workflows/ci.yml` own that evidence.
- For a pull request, inspect `gh pr checks` instead of reproducing heavy work.

Record every command and result, and every required check that was not run.

## 4. Review what gates do not prove

Read each changed file and inspect:

- correctness, failure handling, and deterministic behavior;
- **database ownership** — no DDL against the Copilot-owned `session-store.db`, no Narnia-owned
  data written into `~/.copilot`, no edit to an already-applied migration script;
- **override honesty** — no UI that displays an overridden value without also showing the
  recorded `session-store` value;
- architecture and trust-boundary compatibility, including accepted ADRs;
- manifest, schema, and generated-output consequences, including `mkdocs.yml` navigation and
  `Directory.Packages.props` version pinning;
- dependency drift and unsupported version changes;
- tests or gates missing for introduced behavior;
- docs and instruction authority, and whether prose still states current truth;
- credentials, untrusted input, destructive actions, and privacy.

### Public repository disclosure

Narnia is public. Verify that tracked files, commit messages, code comments, and pull request
title and body contain no local filesystem paths, home directories, machine or user names,
private repository names or contents, internal service names, credentials, session GUIDs, or
live user data. Treat a leak here as a blocker, because published history is permanent.

## 5. Delivery readiness

When the review precedes marking a pull request ready, additionally confirm against
`docs/design/delivery.md`:

- the title is a single conventional line of at most 72 characters, since it becomes the squash
  commit subject;
- `CI`, `PR title`, and `Review policy` are the checks that gate merge, and the stable `CI` check
  only exists once the pull request has been ready — a draft publishes `Draft CI` instead;
- a `Review policy` failure under `copilot-one-approval` means the current head SHA lacks a
  trusted human approval, not that the code is wrong;
- incomplete, unverified, assumed, or deferred work is either fixed or disclosed in the body.

## 6. Reflect on guidance

Treat review as a bounded feedback loop, not a default instruction-edit trigger.

Recommend a guidance change only when the review shows one significant misstep with material
risk, or repeated evidence of the same avoidable misstep. The lesson must be generalizable,
evidence-backed, and assigned to the correct owner: code or tests for enforceable behavior,
`.github/instructions/` for recurring exact rules, `docs/` for rationale, `docs/adr/` for
costly-to-reverse decisions, this skill for procedure, and `AGENTS.md` only for safeguards needed
before any file is selected.

Review remains read-only. Report no guidance change when the threshold is not met.

## 7. Report

Open with:

- `Scope:` reviewed range or paths;
- `Verdict:` `Approve`, `Approve with nits`, or `Request changes`;
- `Validation:` observed, passed, failed, and not-run evidence;
- `Guidance reflection:` `no change warranted` or one candidate with its evidence and owner.

Group introduced findings by severity:

- **Blocker** — broken behavior, security or destructive risk, public disclosure of private
  context, failing required validation, or violation of an accepted ADR or delivery boundary.
- **Major** — clear correctness or contract defect that should be fixed before merge.
- **Minor** — bounded maintainability, coverage, or guidance defect.
- **Nit** — optional polish only.

Every finding includes:

`severity - file:line - issue - governing source - concrete fix`

If there are no introduced findings, say so plainly. State uncertainty and missing evidence
instead of implying an unrun check passed.
