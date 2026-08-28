# Correlate a started run back to its issue in the web

`wayfinder:grilling`

**Closed** — resolution below.

## Question

How is a run linked back to the issue/queue item that started it, so the web shows "this run came from this issue"?

Resolve:
- The correlation id: what it is (queue-item id? issue ref?) and where it's generated.
- How it's threaded from the queue claim → `start-job.ps1` (via `-Spec` or a new param) → into run lifecycle events (`factory-report.ps1` / event payload) → the `runs` row.
- How the web joins queue item ↔ run to render the link (queue row stores the `runId`, or run event carries the queue-item id back).
- Minimal change to existing scripts/event contract — reuse the current event path, don't fork it.

## Blocked by

- [Backend data model: issues projection and ordered run queue](03-backend-data-model.md)

## Resolution (closed)

Most of this was already decided by tickets 03/04; this ticket confirms the link and the one code change.

**The runId IS the correlation id.** Backend generates it at claim (`claim-next`), stores it on `queue.run_id`, and returns it to the daemon. The web joins **`queue.run_id → runs.run_id`** client-side. **Nothing new travels through run events** — the existing event contract is untouched.

**Only code change:** `start-job.ps1` gets an **optional `[string]$RunId` param defaulting to `[guid]::NewGuid().ToString()`** (line 55 becomes the param default). The daemon supplies the runId from `claim-next`; hand-running with no `-RunId` is byte-identical to today (self-generated). `$RunId` already flows to every `Send-FactoryEvent` and `watch-heartbeat.ps1 -RunId`, so no other change.

**Claim reserves `run_id` only — `run-started` creates the runs row.** This refines ticket 04's tentative "pre-create a launching row": `claim-next` only sets `queue.run_id` (a reservation) and returns the id; it does **not** insert a runs row. `start-job.ps1`'s existing `run-started` event creates the runs row (idempotent `INSERT ... ON CONFLICT DO NOTHING`), carrying repo/branch/spec/model the backend doesn't know at claim time. One writer owns the runs row; no double-insert; no synthesized branch. Brief window where `queue.run_id` points at a not-yet-existing runs row → web shows "starting…". Flow diagrammed and approved.

**Handoffs**
- Ticket 04: correct the "claim inserts a launching runs row" note — `claim-next` sets `queue.run_id` only; `run-started` creates the row.
- Ticket 06: web shows "starting…" for a queue item whose `run_id` is set but has no runs row yet.
