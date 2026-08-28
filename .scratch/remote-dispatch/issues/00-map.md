# Map: Remote dispatch — start issues from the web, run on this PC

`wayfinder:map`

## Destination

A written **spec**, ready to hand off to a build, for **triggering factory jobs from the web frontend while the VM still runs on this PC**. A persistent **local client** on the Windows host does two outbound jobs: (1) syncs GitHub issues labelled `ready-for-agent`, from a locally-configured set of repos, up to the Render backend; (2) polls the backend for start requests and invokes the existing `start-job.ps1`. The web shows two lists — synced eligible issues, and an ordered **run queue** the user drags to prioritize. A **"start next"** button claims the highest-ranked `queued` item; the client drains one at a time. `done` items leave the queue, `failed` items stay (restartable by flipping back to `queued`). Runs are correlated back to their issue in the web. **This PC never accepts inbound connections** — it is strictly an outbound polling client. No production code is built by this map; it produces the spec.

## Notes

- **Plan, don't do.** Tickets resolve decisions and end in a handoff spec. No production code built in the map (prototype tickets may make throwaway artifacts).
- **Settled at charting time** (do not re-litigate):
  - Destination is a **spec**, not a build.
  - **No inbound to this PC** — the client is outbound-only, poll-based (symmetric with the dashboard's existing `/runs` poll and outbound event POSTs).
  - Issues come from **GitHub Issues**, not local markdown. Only `ready-for-agent`-labelled issues sync.
  - **One persistent client daemon** on the host, single loop: sync issues up → poll for start requests → invoke `start-job.ps1` unchanged.
  - The **watched-repo list is a local config file** on the host (the PC owns what it watches).
  - Two lists in the web: synced eligible issues, and a separate **ordered run queue** (explicit enqueue). Queue order persisted via a **rank column** in Postgres; drag-drop PATCHes order.
  - MVP is **one run at a time**, **manual "start next"** button (no auto-drain). Start-next is **rejected while any item is `running`**.
  - Queue item lifecycle: `queued → running → done|failed`. `done` **leaves** the queue; `failed` **stays** and can be flipped back to `queued`. "start next" takes highest-ranked `queued`, skipping `running`/`failed`.
  - Runs **correlate** back to the originating issue/request via an id threaded through `start-job.ps1`/run events.
- **Domain:** see `AGENTS.md`, `docs/agents/`, `docs/adr/`. Existing system: `start-job.ps1`, `start-issue.ps1`, `factory-report.ps1`, `dashboard/src/Api` (ASP.NET + Postgres, `runs` table, `X-Factory-Token` ingest), `dashboard/src/web` (Vue, polls `/runs`).
- **Predecessor:** this effort was foreseen in [factory-dashboard map](../../factory-dashboard/issues/00-map.md) under "Dispatch of work packages" / "Dispatching from the UI".
- **Skills:** consult `/grilling` and `/domain-modeling` for decision tickets; `/research` for research tickets; `/prototype` for UI/behaviour tickets.

## Decisions so far

<!-- one line per closed ticket -->

- [Sync GitHub issues from watched repos to the backend](01-github-issue-sync.md) — REST per-repo `issues?labels=ready-for-agent&state=open` (drop PRs); fine-grained PAT at `.secrets/github-pat.txt`; 5-min poll (~1% of rate budget); store `id/repo/number/title/html_url/labels/updated_at/body/state`; full-replace-per-repo snapshot each cycle (delete-not-present + upsert on GitHub `id`).
- [Shape of the local client daemon on the Windows host](02-local-client-daemon.md) — foreground `dispatch-client.ps1` loop (Ctrl-C to stop); gitignored `dispatch-client.config.json` (repos, backendUrl, intervals, model; token via `.secrets/`); single loop at `pollIntervalSec`, sync only when `syncIntervalSec` due; `start-job.ps1` invoked **synchronously inline** so the blocking call is the one-at-a-time mutex; per-phase try/catch log-and-continue; daemon generates the runId and passes a new `-RunId` param to `start-job.ps1`.
- [Backend data model: issues projection and ordered run queue](03-backend-data-model.md) — two idempotent tables: `issues` (PK GitHub `id`, `labels text[]`, full-replace-per-repo sync) and thin `queue` (`queue_id/issue_id/rank/run_id/enqueued_at` + `start_requested_at` from ticket 04); **reference not copy** (web joins for live issue text); **status inferred** from the linked run, no `status` column; integer rank bulk-rewritten on reorder; **backend generates runId atomically at claim** (`UPDATE … run_id=gen_random_uuid() … RETURNING`, prevents double-claim — refines tickets 02/05); `done` deletes the queue row from the event fold (no-op if absent), `failed` stays; manual start means no blocking logic.
- [Backend API contract for sync, queue, and start-next](04-backend-api-contract.md) — public GETs `/issues` + `/queue` (joined for live text); daemon token endpoints `PUT /issues/{repo}` (full-replace sync) and atomic `POST /queue/claim-next` (poll+claim, returns runId); open web verbs `POST /queue` (enqueue), `PATCH /queue/order` (bulk rank), `DELETE /queue/{id}`, `POST /queue/{id}/start-next` (sets `start_requested_at` intent), `POST /queue/{id}/restart` (clears run_id); **web writes open (no token)**, daemon writes carry `X-Factory-Token`; status callbacks reuse existing `POST /runs/{runId}/events` — no new surface.
- [Correlate a started run back to its issue in the web](05-run-issue-correlation.md) — the **runId is the correlation id**; web joins `queue.run_id → runs.run_id`, nothing new in run events; only code change is an optional `-RunId` param on `start-job.ps1` (defaults to new GUID, so hand-runs unchanged); **claim reserves `run_id` only**, `run-started` creates the runs row (one writer, no double-insert, backend needn't synthesize a branch); brief "starting…" gap. Flow diagrammed and approved.
- [Web UI: two lists, drag-to-prioritize, start-next](06-web-ui.md) — **Variant A won** (two columns side-by-side): eligible issues left with per-row Enqueue, ordered queue right with drag-to-rank, inferred-status pills, "start next" disabled while running, restart-on-failed + remove, `run ↗` correlation link, struck-through "no longer eligible" on join-miss. New `DispatchView/` theme folder reusing `RunView` pill styling. Prototype (3 variants) captured on branch `prototype/dispatch-ui`.

**Destination reached** — all six tickets resolved. The way to a buildable spec is clear: a persistent outbound `dispatch-client.ps1` daemon syncs `ready-for-agent` GitHub issues up and polls/claims start requests, invoking `start-job.ps1 -RunId` unchanged; the ASP.NET/Postgres backend gains `issues` + thin `queue` tables and sync/queue/claim endpoints (web writes open, daemon token); the Vue dashboard gains Variant-A dispatch view. Hand off to a build (e.g. `to-tickets`).

## Not yet specified

- **How the client authenticates its outbound writes/reads to the backend** — RESOLVED by ticket 04: daemon reuses `X-Factory-Token`; web writes are open.
- **Open web-write exposure** — enqueue/reorder/start-next take no auth (matches the public-dashboard stance); acceptable for a private tool, revisit if the dashboard ever goes multi-user or public-internet.
- **Production hardening of the daemon** — wrapping `dispatch-client.ps1` in Task-Scheduler-at-logon or a Windows service so it survives reboots; deferred until the foreground loop is trusted.

## Out of scope

- **Parallelization** — running more than one issue at a time; explicitly MVP-later.
- **Authoring/editing issues from the web** — issues are authored in GitHub (or locally by an AI and pushed to GH); the web only lists and queues.
- **Auto-drain of the queue** — deferred; MVP is a manual "start next" button so the user can merge PRs between runs.
