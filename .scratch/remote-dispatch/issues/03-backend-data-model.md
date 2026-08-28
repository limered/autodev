# Backend data model: issues projection and ordered run queue

`wayfinder:grilling`

**Closed** — resolution below.

## Question

What does the Render Postgres store to support the two web lists, and what are the tables?

Resolve:
- **Issues projection** table: the synced `ready-for-agent` issues (keyed by repo + issue number?), fields from the sync ticket, upsert-on-sync, and how removed/closed issues are reconciled.
- **Run queue** table: the ordered queue distinct from the issues list. A **rank column** for drag ordering; status lifecycle `queued → running → done|failed`; link to the source issue; correlation to the resulting run.
- Terminal rules encoded: `done` **leaves** the queue (delete row? or filtered out?); `failed` **stays**, restartable by flipping to `queued`.
- Relationship to the existing `runs` table — does the queue row reference a `runId`, and when is that id known (host-generated at claim time)?
- Whether enqueue (issue → queue) is a copy or a reference.

Keep it consistent with the existing event-sourced `runs` design where sensible.

## Blocked by

- [Sync GitHub issues from watched repos to the backend](01-github-issue-sync.md)

## Resolution (closed)

Two tables added to the existing Render Postgres, following the current `RunsSchema` pattern (idempotent `CREATE TABLE IF NOT EXISTS`, no migration framework).

### `issues` — synced projection
- **PK = GitHub `id` (bigint)** — globally unique, no composite key.
- Columns: `id bigint PK`, `repo text`, `number int`, `title text`, `html_url text`, `labels text[]`, `body text` (nullable), `state text`, `updated_at timestamptz`, `synced_at timestamptz`.
- **Sync = full-replace-per-repo** in one transaction: `DELETE FROM issues WHERE repo=@repo AND id <> ALL(@ids)` then upsert each. Handles closes/relabels/deletes for free (ticket 01).

### `queue` — ordered run queue (thin, reference not copy)
- Columns: `queue_id uuid PK`, `issue_id bigint` (→ `issues.id`), `rank int`, `run_id uuid` (nullable, → `runs.run_id`), `enqueued_at timestamptz`. **(Ticket 04 adds `start_requested_at timestamptz` nullable — the start-next intent; fold into this table's `CREATE TABLE`.)**
- **Reference, not copy** (Q2): the row references the issue by `id` and the web joins for current text — so a later edit to the issue body (e.g. a finding changed it) is reflected without dequeue/enqueue. A queued issue relabelled/closed out of the projection can disappear from the join; the web shows it as no-longer-eligible (handled in web ticket 06), the queue row stands.
- **Status is inferred, not stored** (Q3): row exists + `run_id NULL` = queued; `run_id` → `runs.status` gives running/failed/etc.; `done` = row deleted. No `status` column.
- **Rank** (Q5): integer; reorder = bulk rank rewrite in one transaction; enqueue appends at `MAX(rank)+1`; drag persists via PATCH (ticket 04).
- **Manual start, no blocking logic** (Q6): start is manual, so a `failed` item needs no wall/blocking machinery — the user reads the queue and restarts what they choose. Failed state is merely *visible* via the linked run, not enforced. Restart = clear `run_id` (set NULL) so the item is claimable again.

### Correlation / runId (Q4 — **refines tickets 02 & 05**)
- The **backend generates the runId at claim time**, not the daemon. "start next" claim is atomic: `UPDATE queue SET run_id = gen_random_uuid() WHERE queue_id = @head AND run_id IS NULL RETURNING run_id` — this both prevents double-claim and hands the daemon its runId in one round-trip. Backend also creates the `runs` row (`launching`). Daemon receives the runId and passes it to `start-job.ps1 -RunId`.
- Supersedes ticket 02's tentative "daemon generates the runId." Ticket 05 still owns adding `-RunId` to `start-job.ps1`.

### `done` cleanup (Q8)
- When `POST /runs/{runId}/events` folds a run to **`done`**, the same handler runs `DELETE FROM queue WHERE run_id = @runId` — a **no-op if no row matches** (hand-run `start-job.ps1` jobs have no queue row). `failed` does **not** delete (item stays for manual restart).

**Handoffs**
- Ticket 04: claim endpoint = the atomic `UPDATE ... RETURNING run_id`; reorder = bulk rank PATCH; enqueue/remove/restart(=clear run_id) endpoints.
- Ticket 05: add `-RunId` param to `start-job.ps1`; runId now comes from the backend claim response.
- Ticket 06: web joins `queue` → `issues` for live text; show "no longer eligible" when the join misses.
