# Specify host-side reporting integration

`wayfinder:grilling`

**Status:** closed

**Assignee:** driver (claimed)

**Blocked by:** Design the ingest API contract.

## Question

Specify exactly where and how `start-job.ps1` and `watch-heartbeat.ps1` are modified to emit lifecycle events to the ingest API. Identify each emit point (launch, provisioning done, run started, each heartbeat/stale check, stall detected, freeze captured, PR verified, done, failed, teardown), what payload each sends, how the shared secret and backend URL are configured on the host (env vars? `.secrets/`?), and — critically — how reporting failures are handled so a dashboard outage never breaks or blocks a real job run (fire-and-forget, bounded timeout, swallow errors). Output: the integration spec, not the code.

## Resolution

### Config (host)

- Backend URL + shared token live in **`.secrets/`** (e.g. `.secrets/factory-dashboard-url.txt`, `.secrets/factory-dashboard-token.txt`), read once at `start-job.ps1` startup — consistent with the existing PAT / API-key files.
- **If either file is absent, reporting is disabled** (every emit is a silent no-op) so the scripts still run standalone.

### Reporting helper — `Send-FactoryEvent`

- Lives in a small shared `factory-report.ps1`, dot-sourced by both `start-job.ps1` and `watch-heartbeat.ps1`.
- Signature: `Send-FactoryEvent -RunId <guid> -Type <string> [-Fields <hashtable>]`.
- POSTs `{ type, at = (UtcNow ISO-8601), ...Fields }` to `"$Url/runs/$RunId/events"` with header `X-Factory-Token: <token>`, **5s timeout**, wrapped in `try/catch` that **swallows all errors and never throws** (writes at most a `WARN` line). Fire-and-forget: a dashboard outage can never break or block a real job.
- No-op immediately if config is unset.

### runId + who emits what

- `start-job.ps1` generates `$RunId = [guid]::NewGuid()` at startup and **owns all lifecycle emits**.
- The background `Start-Job` agent process is **untouched** — it only `touch`es `/tmp/heartbeat` as today.
- `watch-heartbeat.ps1` is passed `$RunId` + config and emits **heartbeat** and **stall-detected** directly (it already reads the marker mtime each poll — the natural emit point).

### Emit points

| Location | When | Event `type` | Fields |
|---|---|---|---|
| start-job, startup (after arg parse) | run begins | `run-started` | repo, branch, spec, model |
| start-job, after `$vmJob` starts / watcher entry | agent run begins | `agent-started` | vmName |
| watch-heartbeat, poll where marker mtime **advanced** since last reported | agent produced output | `heartbeat` | *(none; `at` carries the timestamp)* |
| watch-heartbeat, on stall throw | heartbeat went stale | `stall-detected` | failureReason (`"heartbeat stale {n}s"`) |
| start-job, after `Save-FreezeSnapshot` | freeze written | `freeze-captured` | freezeLocalPath |
| start-job, after PR verified | PR found | `pr-verified` | prUrl |
| start-job, end of `try` (success) | run finished ok | `run-finished` | *(none)* |
| start-job, `catch` block | run failed | `run-failed` | failureReason (`"$_"`) |

- **Heartbeat rate:** emit **only when the marker mtime advanced** since the last reported value — a truly idle agent stops reporting, which is exactly the signal wanted. No fixed-interval spam.
- **Teardown** (VM destroy) emits nothing — it's implied by the terminal `run-finished`/`run-failed` already sent. The backend's sticky-terminal rule protects against any late event.
- Ordering/dupes are the backend's problem (timestamp-guarded fold); the host just fires events with an accurate `at`.
