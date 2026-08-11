---
description: How changes reach Narnia's main branch — feature branches, pull requests, draft and ready validation scope, required checks, and the sanitization rules for a public repository.
---

# Delivery

Changes reach `main` through pull requests. Local commits are unrestricted checkpoints; nothing
is pushed directly to the default branch.

The machine-readable contract lives in
[`.github/genesis-delivery.json`](https://github.com/ncosentino/narnia/blob/main/.github/genesis-delivery.json)
and is the source of truth for the default branch, required checks, and title rules. When this
page and that file disagree, the file wins.

## Pull request titles

`.github/workflows/pr-title.yml` publishes the **PR title** check. A title must be a single line
of at most 72 characters matching a conventional pattern such as `feat(scope): description`.

The limit is not cosmetic. Merges are squash merges configured to take the pull request title as
the commit subject and the pull request body as the commit message, so the title becomes
permanent Git history.

## Draft and ready validation

`.github/workflows/ci.yml` resolves a validation scope before running anything, then renames its
final gate job according to the pull request's draft state. That gives two distinct check names
for the same workflow:

| Pull request state | Scope | Jobs that run | Gate check name |
|---|---|---|---|
| Draft | `subset` | Build and test .NET | `Draft CI` |
| Ready | `full` | Build and test .NET, release package, documentation | `CI` |

The draft subset builds and tests on Windows but skips release packaging and documentation
validation. Marking a pull request ready starts fresh full validation and publishes the stable
`CI` check that branch protection requires.

Because only the ready run publishes `CI`, a pull request that was never marked ready has not
produced the check that gates merge.

## Required checks

Branch protection on `main` requires the aggregate gates, not individual job names:

- `CI`
- `PR title`
- `Review policy`

Requiring aggregates rather than job names keeps the workflow's internal job graph free to change
without a branch-protection edit.

## Review policy

`.github/workflows/pr-review-policy.yml` publishes the **Review policy** check and evaluates the
repository variable `GENESIS_REVIEW_POLICY`.

Under `copilot-one-approval`, a ready pull request authored by the Copilot bot needs one approval
from an OWNER, MEMBER, or COLLABORATOR **on the current head SHA**. Approvals are matched against
the exact commit, so pushing another commit invalidates the previous approval and requires a new
one. Draft pull requests are not blocked; the requirement applies when they become ready.

Approving a workflow run from an external fork authorizes the proposed workflow as a whole,
including which runners it selects. Review workflow changes and runner routing before approving
execution. Narnia's pull request validation uses GitHub-hosted runners.

## Before marking a pull request ready

Ready means ready for review. Before moving a pull request out of draft, audit the change for
what is incomplete, unverified, assumed, or deliberately deferred.

Fix anything blocking — known broken behavior, failing or unrun validation, unimplemented scope —
or leave the pull request in draft. Disclose the rest in the pull request body: implementation
gaps, technical debt, missing coverage, weak assertions, and assumptions.

Run [`review-changes`](https://github.com/ncosentino/narnia/blob/main/.github/skills/review-changes/SKILL.md)
before delivery. It resolves the diff, the applicable instructions, the declared validation
surfaces, and existing CI evidence.

## Public repository hygiene

Narnia is a public repository. Everything published from it — tracked files, commit messages,
code comments, pull request titles and bodies, and issue comments — is world-readable and
permanent.

Before publishing, remove local and private context:

- No local filesystem paths, home directories, machine names, or user names.
- No private repository names, contents, structure, or issue references.
- No internal service names, hostnames, or environment values.
- No credentials, tokens, or live identifiers, including session GUIDs and real user data.

Use public sources and sanitized placeholders instead. This applies to evidence pasted into a
pull request body just as much as to source files.

## Releases

`.github/workflows/release.yml` owns tag-triggered release publishing and is not part of pull
request validation. See [Building from Source](../building.md) for producing and smoke-testing
the Windows release package locally with the same script CI uses.
