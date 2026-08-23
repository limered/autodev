# Map: Factory instance dashboard on Render

`wayfinder:map`

## Destination

A written **spec**, ready to hand off to a build, for a **read-only observability dashboard** hosted on Render that shows one row per `start-job.ps1` job run (repo, branch, spec, model, status, last heartbeat, PR url, freeze link) across all instances. The Windows host pushes lifecycle state outbound to a Render-hosted **.NET backend**; a **Vue** frontend polls it. The dashboard is **public to read**; the ingest endpoint is protected by a shared secret. No UI code is built by this map — it produces the spec.

## Notes

- **Plan, don't do.** Tickets resolve decisions and end in a handoff spec. No production code built in the map (prototype tickets may make throwaway artifacts).
- **Settled at charting time** (do not re-litigate): .NET backend + Vue frontend; dashboard public read, no login; host reports by **modifying the existing scripts** (`start-job.ps1`, `watch-heartbeat.ps1`); UI updates by **polling**; ingest/write endpoint protected by a **shared secret token**; instance = one `start-job.ps1` run.
- **Domain:** see `AGENTS.md`, `docs/agents/`. Existing system: `start-job.ps1`, `watch-heartbeat.ps1`, `infrastructure/multipass/`, `.scratch/freezes/`.
- **Skills:** consult `/grilling` and `/domain-modeling` for decision tickets; `/research` for research tickets.

## Decisions so far

<!-- one line per closed ticket -->

- [Research Render hosting for .NET API + Vue static frontend](02-research-render-hosting.md) — .NET is Docker-only; free web services spin down (15 min) and lose in-memory state; free Postgres expires in 30 days so durable state needs a paid tier; same-origin single-service deploy avoids CORS; outbound POST from a NAT host is trivial.
- [Define the job-run state model](01-job-run-state-model.md) — run keyed by a host-generated `runId` GUID; 5 statuses (launching→running→done|failed, with stalled a transient→failed); discrete lifecycle events folded into status; freeze carried as flag + local host path (not remotely viewable).
- [Choose the backend state storage](03-choose-storage.md) — managed paid Postgres (single `runs` table, upsert-per-event); web service is stateless so it runs on the **free tier** (all durability in Postgres), Starter optional only to avoid cold-start dropping the first event.
- [Design the ingest API contract](04-ingest-api-contract.md) — event-per-call `POST /runs/{runId}/events` guarded by `X-Factory-Token`; timestamp-guarded fold with sticky terminal states; public reads `GET /runs`, `/runs/active`, `/runs/{runId}`.
- [Specify host-side reporting integration](05-host-reporting-integration.md) — shared `Send-FactoryEvent` helper (5s timeout, swallows all errors, no-op if `.secrets/` config absent); `start-job.ps1` owns runId + lifecycle emits, `watch-heartbeat.ps1` emits heartbeat (only when mtime advances) + stall; agent background job untouched.
- [Specify the Render deployment and config](06-render-deployment-spec.md) — all dashboard code lives under a root `dashboard/` folder; single same-origin Docker web service (ASP.NET serves API + built Vue `dist`, no CORS), configured **manually in the Render UI** (Root Directory `dashboard`, health check `/health`) with a manually-created paid Postgres; `render.yaml` Blueprint was dropped as it conflicted with Root-Directory handling.

**Destination reached** — all decision tickets resolved. The way to a buildable spec is clear; handoff to a build.

## Not yet specified

- **Dispatch of work packages** — a future effort, but the state model chosen now should not actively preclude it. Watch for accidental one-way-door choices. (State model keeps this open: opaque runId + discrete events.)

## Out of scope

- Dispatching / launching jobs from the UI — deferred to a future effort per destination.
- Authenticated / private dashboard access — ruled out; dashboard is public read.
