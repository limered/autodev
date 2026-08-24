# 04 — Semantic folder structure and test setup for the frontend

**What to build:** The frontend gains a semantic folder convention — folders grouped by theme, not by kind. Code for a theme lives together under that theme's folder split by role: e.g. `RunView/components/`, `RunView/models/`. Shared code lives under `_shared/` (`_shared/components/`, `_shared/services/`, `_shared/models/`). The rule for promotion: a component or model starts inside its owning theme folder and only moves to `_shared/` the first time a second theme needs it — nothing is placed in `_shared/` speculatively.

Tests do not sit inline beside the code. Each theme folder has a parallel `_tests/` folder mirroring its structure — a file at `RunView/models/runView.js` is tested by `RunView/_tests/models/runView.test.js`.

The existing run view-model (`runView.js` + its test, landed in deepening ticket 01) moves into the `RunView` theme folder under this convention: the module under its role folder, its test under the parallel `_tests/`. Vitest (already wired via `npm test`) keeps working after the move. The convention is written down so future frontend work follows it, and the factory runs the frontend tests as a declared harness.

**Blocked by:** 01 — Deepen the Run view-model module (done).

**Status:** ready-for-agent

- [x] A documented convention exists: theme folders split by role (`RunView/components`, `RunView/models`), shared code under `_shared/`, promote-to-`_shared` only on second use _(recorded in AGENTS.md → Frontend structure)_
- [x] Tests live in a parallel `_tests/` folder per theme, mirroring the code structure (`RunView/models/x.js` → `RunView/_tests/models/x.test.js`)
- [x] `runView.js` moves under the `RunView` theme folder; its test moves to the parallel `_tests/`
- [x] `App.vue` / imports updated so the app builds and runs after the move
- [x] `npm test` (vitest) passes against the relocated tests
- [x] A `test-harness.web` entry in AGENTS.md runs the frontend suite so the factory test phase covers it
