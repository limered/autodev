# Web UI: two lists, drag-to-prioritize, start-next

`wayfinder:prototype`

**Closed** — resolution below.

## Question

How should the web frontend present and drive this, and how does it feel?

Prototype and decide:
- **Two lists**: synced eligible `ready-for-agent` issues (across repos) and the ordered run queue. Layout and how you move an issue from eligible → queue (explicit enqueue).
- **Drag-to-prioritize** the queue; where the running/failed items sit and how their status reads.
- **"Start next"** button: placement, disabled state while a run is `running`, feedback after click.
- **Restart** a `failed` item (flip to `queued`) and remove from queue.
- **Correlation**: how a queue item links to its live/finished run (jump to the existing run card).
- Fits the existing Vue polling dashboard and the repo's semantic-by-theme frontend structure (`AGENTS.md`).

Make a throwaway prototype to react to before speccing. Link it as an asset.

## Blocked by

- [Backend data model: issues projection and ordered run queue](03-backend-data-model.md)
- [Backend API contract for sync, queue, and start-next](04-backend-api-contract.md)

## Resolution (closed)

Prototyped 3 structurally-different variants (`.scratch/remote-dispatch/prototype/dispatch-ui.prototype.html`, mock data, read-only, reuses the Factory Dashboard palette; primary source on branch `prototype/dispatch-ui`).

**Verdict: Variant A — two columns side-by-side.** Eligible `ready-for-agent` issues on the left, ordered run queue on the right.

**Layout & behaviours to build:**
- **Left column — eligible issues** (`GET /issues`, across repos): title + `repo#number`, an **Enqueue →** button per row (`POST /queue`). Issues already in the queue are omitted.
- **Right column — run queue** (`GET /queue`, ordered by rank): each row = drag grip + rank + issue title/repo + **status pill** + run link + row actions.
  - **Drag to prioritize** → `PATCH /queue/order` (bulk rank rewrite on drop).
  - **Status is inferred** (ticket 03): no linked run = `queued`; linked run `launching/running` = `running`; `failed`/`done` from the run; the brief reserved-but-no-runs-row gap shows as `starting` (ticket 05).
  - **Start next** button above the queue, **disabled while any item is `running`** (one-at-a-time) → `POST /queue/{id}/start-next`.
  - **Restart** action shown only on `failed` items → `POST /queue/{id}/restart`; **Remove** on all → `DELETE /queue/{id}`.
  - **Correlation**: a `run <id> ↗` link jumps to the existing run card (join `queue.run_id → runs.run_id`).
  - **No-longer-eligible**: a queued item whose issue the sync has dropped (join miss) renders struck-through with a "no longer eligible" note — reference-not-copy consequence (ticket 03), item still removable/runnable.

**Structure notes for the build:** fits the existing Vue polling dashboard; new theme folder `DispatchView/` (components/models/services) per the repo's semantic-by-theme layout (`AGENTS.md`), reusing the run-status pill styling from `RunView`. HTML5 drag-drop or a tiny sortable is enough for a hand-sized queue.
