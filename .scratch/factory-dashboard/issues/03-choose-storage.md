# Choose the backend state storage

`wayfinder:grilling`

**Status:** closed

**Assignee:** driver (claimed)

**Blocked by:** Define the job-run state model; Research Render hosting for .NET API + Vue static frontend.

## Question

Where and how does the .NET backend persist job-run state — managed Postgres, SQLite on a disk, or in-memory (ephemeral, lost on redeploy)? Decide based on the freshness need (is "what's live now" enough, or must finished runs survive restarts?), Render's storage options and cost (from the hosting research), and the retention question in the map's Not-yet-specified. Output: the storage choice and, if persistent, the schema for the job-run model.

## Resolution

**Managed paid Postgres**, with the .NET service on a **paid always-on Starter** instance. History must persist across restarts/redeploys, and a paid Render plan is already in place — which removes the free-tier blockers (in-memory loss, 15-min spin-down, 30-day free-Postgres expiry).

- **Database:** paid **basic Postgres** (backups, no expiry). Not free Postgres (expires ~30 days, loses history).
- **Web service:** paid **Starter** (never spins down) so the host's fire-and-forget POSTs always hit a warm endpoint — no ~1 min cold start, no first-POST timeout after idle.
- **Not** in-memory (loses state on restart) and **not** SQLite-on-disk (forces a single instance and disables zero-downtime deploys; Postgres is lower-friction and already affordable here).

### Schema (single table)

One row per run, keyed by the host-generated `runId`. Discrete events UPSERT/patch this row.

```sql
CREATE TABLE runs (
    run_id            uuid        PRIMARY KEY,          -- host-generated
    repo              text        NOT NULL,             -- owner/name
    branch            text        NOT NULL,
    spec              text        NOT NULL,
    model             text        NOT NULL,
    vm_name           text,                             -- may be null at first event
    status            text        NOT NULL,             -- launching|running|stalled|done|failed
    started_at        timestamptz NOT NULL,
    finished_at       timestamptz,
    last_heartbeat_at timestamptz,
    pr_url            text,
    failure_reason    text,
    freeze_captured   boolean     NOT NULL DEFAULT false,
    freeze_local_path text,                             -- host path; not remotely viewable
    updated_at        timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX runs_status_started_idx ON runs (status, started_at DESC);
```

- `status` kept as text (matches the 5-value model); can be a CHECK constraint or enum at build time.
- No separate events table — the fold-into-status model means the backend patches columns per event; an events audit log is out of scope for now (add later if needed).
- **Retention:** history persists indefinitely; pruning is optional/manual, not a launch requirement. Resolves the retention fog patch.
