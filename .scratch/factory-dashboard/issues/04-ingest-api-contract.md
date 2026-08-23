# Design the ingest API contract

`wayfinder:grilling`

**Status:** closed

**Assignee:** driver (claimed)

**Blocked by:** Define the job-run state model.

## Question

Define the HTTP contract the Windows host posts lifecycle events to, and the read API the Vue frontend polls. Decide: endpoint shape (event-per-call like POST /runs/{id}/events, vs upsert the whole run), the shared-secret auth mechanism (header name, how the token is provisioned on host and backend), the read endpoint(s) and their JSON shape, idempotency/ordering (events may arrive out of order or be retried), and how a run is identified (vm name? a host-generated run id?). Output: request/response schemas for ingest and read.

## Resolution

### Auth

- Write endpoints require header **`X-Factory-Token: <secret>`**; backend compares against an env var and returns **401** if missing/wrong.
- Read endpoints are **public** (no token) — dashboard is public read.
- Token provisioning is the host-reporting / deployment tickets' job (host env/`.secrets`, backend env var).

### Ingest — event-per-call

`POST /runs/{runId}/events`  (auth required)

```json
{
  "type": "run-started | agent-started | heartbeat | stall-detected | freeze-captured | pr-verified | run-finished | run-failed",
  "at":   "2026-08-23T10:15:03Z",          // host clock, ISO-8601 UTC — the ordering key
  // type-specific fields, all optional except where a type needs them:
  "repo": "owner/name", "branch": "...", "spec": "...", "model": "...",  // run-started
  "vmName": "factory-job-...",             // agent-started
  "prUrl": "https://github.com/...",       // pr-verified
  "failureReason": "heartbeat stale 317s", // stall-detected / run-failed
  "freezeLocalPath": ".scratch/freezes/...", // freeze-captured
}
```

- **Response:** `202 Accepted` (empty body) on success; `401` bad token; `400` malformed.
- **First event** (`run-started`) upserts the row for `runId`; later events patch it.
- Assets (freeze contents) are **not** uploaded — only the local path string is carried.

### Fold rules (idempotency / ordering)

Events are fire-and-forget and may retry or arrive out of order. Backend folds defensively:

1. **Timestamp-guarded:** a field is only written if the event's `at` is newer than the value's last-applied timestamp (per-field or against `updated_at`). Stale retries and duplicates become no-ops.
2. **Sticky terminal:** once `status` is `done` or `failed`, no later event may change `status` (a late `heartbeat` can't un-finish a run). Informational fields still obey the timestamp rule.
3. Status is derived from event type per the state-model transitions; `heartbeat` only updates `last_heartbeat_at`.

### Read — polled by Vue UI (public)

- `GET /runs` → all runs, **newest `started_at` first**, JSON array of the run object.
- `GET /runs/active` → only non-terminal runs (`launching|running|stalled`) — the cheap "live" view as history grows.
- `GET /runs/{runId}` → single run, `404` if unknown.

Run object shape (maps to the `runs` table):

```json
{
  "runId": "uuid",
  "repo": "owner/name", "branch": "...", "spec": "...", "model": "...",
  "vmName": "...|null",
  "status": "launching|running|stalled|done|failed",
  "startedAt": "iso", "finishedAt": "iso|null",
  "lastHeartbeatAt": "iso|null",
  "prUrl": "...|null",
  "failureReason": "...|null",
  "freezeCaptured": false,
  "freezeLocalPath": "...|null",
  "updatedAt": "iso"
}
```

UI polls `/runs` (or `/runs/active`) every few seconds and renders the table; staleness ("last seen Ns ago") is computed client-side from `lastHeartbeatAt`.
