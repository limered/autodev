# 02 — One event, host to screen (tracer bullet)

**What to build:** The thinnest complete thread through every layer, proving the whole pipe host → Render → Postgres → screen. Launching a real `start-job.ps1` run causes a single row to appear on the deployed dashboard. Only the `run-started` event exists at this stage — later slices widen the thread.

**Blocked by:** 01 — Scaffold dashboard service + Render Blueprint.

**Status:** ready-for-agent

- [ ] A `runs` table exists (single-table schema keyed by host-generated `runId`), created via migration or idempotent startup.
- [ ] `POST /runs/{runId}/events` accepts a `run-started` event (fields: repo, branch, spec, model), upserts the row in `launching` status, and returns `202`. The endpoint requires header `X-Factory-Token` and returns `401` if missing/wrong.
- [ ] `GET /runs` returns all runs newest-first as JSON.
- [ ] The Vue frontend polls `GET /runs` and renders one row per run showing repo, branch, model, status.
- [ ] A shared `Send-FactoryEvent` PowerShell helper POSTs `{type, at, ...}` with the token header, a short timeout, and swallows all errors (never throws). Backend URL + token are read from `.secrets/`; if absent, reporting is a silent no-op.
- [ ] `start-job.ps1` generates a `runId` GUID at startup and emits `run-started` via the helper.
- [ ] Launching a real job makes a row appear on the deployed dashboard.
