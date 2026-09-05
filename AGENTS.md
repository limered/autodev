> This file contains configuration for AI coding agents working in this repo.

## Agent skills

### Issue tracker

Issues live as GitHub Issues on the `limered/autodev` repo. See `docs/agents/issue-tracker.md`.

### Triage labels

Default canonical labels: `needs-triage`, `needs-info`, `ready-for-agent`, `ready-for-human`, `wontfix`. See `docs/agents/triage-labels.md`.

### Domain docs

Single-context layout: `CONTEXT.md` at the root, ADRs in `docs/adr/`, glossary in `docs/glossary.md`. See `docs/agents/domain.md`.

### Commits

Use the `/atomic-commit` skill to stage and commit changes.

## Test harness

The factory test phase runs every `test-harness.<name>` command declared here. Any failure fails the run.

test-harness.api: dotnet test dashboard/src/Api.Tests
test-harness.web: npm test --prefix dashboard/src/web
test-harness.ps1: pwsh -NoProfile -Command "Invoke-Pester lib/"

## Frontend structure

The frontend (`dashboard/src/web`) groups code **semantically by theme, not by kind**. Do not create top-level `services/`, `models/`, or `components/` folders.

- Each theme owns a folder split by role: `RunView/components/`, `RunView/models/`, `RunView/services/`.
- Shared code lives under `_shared/` (`_shared/components/`, `_shared/models/`, `_shared/services/`).
- **Promote on second use**: a component or model starts in its owning theme folder and moves to `_shared/` only the first time a second theme needs it. Nothing goes in `_shared/` speculatively.
- Tests live in a parallel `_tests/` folder per theme, mirroring the code structure — `RunView/models/runView.js` is tested by `RunView/_tests/models/runView.test.js`. Tests never sit inline beside the code.
