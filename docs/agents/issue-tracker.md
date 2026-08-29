# Issue tracker

This project uses **GitHub Issues on the `limered/autodev` repo** for issue tracking.

## Authoring tickets

Create a ticket as a GitHub issue. The issue body IS the spec: a "What to build"
description followed by an acceptance-criteria checklist (`- [ ]` items). Apply
the `ready-for-agent` label when the issue is ready to be picked up by a factory
job.

## Dependency / blocking edges

Express edges natively on GitHub:

- A task-list reference (`- [ ] blocked by #12`) or a plain "Blocked by #12" line
  in the issue body, or
- GitHub's native issue relationships ("blocked by") where available.

The launcher does not enforce blockers; sequence work by only labelling an issue
`ready-for-agent` once its blockers are closed.

## Launching

`start-issue.ps1` (and the `start-issue` skill) resolve an issue by its GitHub
number, read the body from the GitHub API, and start a VM job that implements it
and opens a PR.

## Migration note

Existing `.scratch/*/issues/` folders are **historical**. They are the local
markdown tracker used before this switch and are kept for reference only; there
is no requirement to back-fill closed feature folders onto GitHub. (Freeze
snapshots under `.scratch/freezes/` are unrelated and stay.)
