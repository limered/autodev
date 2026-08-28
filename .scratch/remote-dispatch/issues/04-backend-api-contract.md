# Backend API contract for sync, queue, and start-next

`wayfinder:grilling`

**Closed** — resolution below.

## Question

What endpoints does the backend expose for the client and the web, and how are they authed?

Resolve:
- **Sync-up** (client → backend): endpoint the client POSTs the current `ready-for-agent` issue set to; full-replace vs delta per repo.
- **Queue reads** (web + client): list issues, list queue ordered by rank.
- **Enqueue / reorder / remove** (web → backend): add an eligible issue to the queue; PATCH rank on drag-drop; remove.
- **Restart** (web → backend): flip a `failed` item back to `queued`.
- **Start next** (web → backend, then client claim): "start next" claims the **highest-ranked `queued`** item, sets it `running`, and is **rejected while any item is `running`**. Decide whether the web sets a "start-requested" intent that the client claims, or the client polls and claims directly. Define the claim protocol so two client polls can't double-claim.
- **Status callbacks** (client → backend): `running → done|failed`, and threading the run correlation id.
- **Auth**: which calls reuse the `X-Factory-Token` shared secret; whether public reads stay public like `/runs`; whether the web needs any auth to write queue order / trigger start-next.

## Blocked by

- [Backend data model: issues projection and ordered run queue](03-backend-data-model.md)

## Resolution (closed)

Minimal-API endpoints in the existing ASP.NET app, matching the current style (public GETs, `X-Factory-Token` on daemon writes, direct Npgsql).

### Auth
- **Daemon writes** (sync, claim, run events) carry `X-Factory-Token` — same trusted host that already POSTs run events.
- **Web writes** (enqueue/reorder/remove/restart/start-next) are **open, no token** — matches the predecessor map's "public dashboard, no login" stance. Exposure noted in map fog; it's a private tool.
- **All GETs public**, like `/runs`.

### Start-next flow (intent → claim, two layers)
- Web click sets an **intent**; the **daemon polls and performs the atomic claim**. Separates "user asked" (web, open) from "host claimed" (daemon, token). Single-claim guaranteed by the atomic UPDATE.
- Intent stored as a nullable **`start_requested_at timestamptz` column on the queue row** (no new table; keeps ticket 03's thin queue; survives API restart on Render free tier). Cleared by claim/remove/restart.

### Endpoints

**Reads (public)**
- `GET /issues` — all synced eligible issues.
- `GET /queue` — queue rows joined to `issues` for live text, ordered by `rank`. Web correlates `queue.run_id` → `/runs` client-side.
- (`GET /runs`, `/runs/active` unchanged.)

**Daemon (token)**
- `PUT /issues/{repo}` — full-replace snapshot for that repo (body = issue array); transactional delete-not-present + upsert on GitHub `id`, scoped to repo (ticket 03). Idempotent, one call per repo per sync cycle.
- `POST /queue/claim-next` — atomic: `UPDATE queue SET run_id = gen_random_uuid(), start_requested_at = ... WHERE queue_id = (highest-ranked item with start_requested_at set AND run_id IS NULL) RETURNING queue_id, run_id` + joined repo/number/body(spec). Returns the claimed job, or empty/204 if nothing requested. Poll-and-claim in one call, no double-claim. Daemon calls each tick. **(Ticket 05: claim only RESERVES `run_id`; it does NOT insert a runs row — `start-job.ps1`'s `run-started` event creates it.)**

**Web mutations (open)** — one action per verb, matching ticket 03 ops:
- `POST /queue` `{issueId}` — enqueue, append at `MAX(rank)+1`.
- `PATCH /queue/order` `{orderedQueueIds[]}` — bulk rank rewrite on drag-drop (one transaction).
- `DELETE /queue/{queueId}` — remove.
- `POST /queue/{queueId}/start-next` — set `start_requested_at` (the intent).
- `POST /queue/{queueId}/restart` — clear `run_id` (re-arm a failed item).

**Status callbacks** — **no new endpoint**. The daemon reports run lifecycle via the existing `POST /runs/{runId}/events` (`factory-report.ps1`). Ticket 03's `done → DELETE queue row WHERE run_id` (no-op if absent) lives in that event fold; `failed` leaves the row for manual restart. Run status flows through the existing path; the queue reacts to it — zero new surface.

**Handoffs**
- Ticket 05: `claim-next` returns the runId; daemon passes it to `start-job.ps1 -RunId`.
- Ticket 06: web consumes `GET /issues` + `GET /queue`; drag-drop → `PATCH /queue/order`; buttons → the `POST /queue/*` verbs; "no longer eligible" when the `queue → issues` join misses.
- Schema (ticket 03) gains `start_requested_at` on the queue table — fold this into the queue `CREATE TABLE`.
