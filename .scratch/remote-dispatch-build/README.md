# Remote dispatch — build

Implementation tickets for starting factory jobs from the web while the VM runs on this PC.

**Source spec:** wayfinder map [`.scratch/remote-dispatch/issues/00-map.md`](../remote-dispatch/issues/00-map.md) (destination reached, all decisions closed).

## Backwards-compatibility contract (applies to EVERY ticket)

These tickets are built *by* the VM factory system, using the existing local flow. So every change is **purely additive to the existing VM flow** and must not regress it:

- The existing `start-job.ps1` / `start-issue.ps1` hand-run path keeps working unchanged.
- The existing `runs` table, `POST /runs/{runId}/events` ingest, and `GET /runs` dashboard keep working unchanged.
- New tables/endpoints/UI are additive; where an existing handler is touched (only the event fold in ticket 05), the change is a **no-op when no queue row is involved** — hand-run jobs are unaffected.

Every ticket carries "local flow unchanged" as an acceptance checkbox; this file is the shared statement of why.

## Dependency order

```
01 start-job -RunId ─┐
02 issues sync ──────┼─> 03 enqueue+queue ─┬─> 04 reorder+remove
                     │                     └─> 05 start-next+daemon ─> 06 restart
                     └─(01 also blocks 05)

07 switch tracker to GitHub ── sequence AFTER 01–06 are implemented
08 full-flow test (created LATER, natively on GitHub, after 07 switches the tracker)
```
