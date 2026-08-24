# 04 — Extract MapRunsEndpoints and slim Program.cs

**What to build:** `Program.cs` becomes bootstrap-only and the run routes move into the feature folder. A `MapRunsEndpoints(this WebApplication)` extension in `Runs/RunsEndpoints.cs` owns all four routes (`POST /runs/{runId}/events`, `GET /runs`, `GET /runs/active`, `GET /health` + SPA fallback stays in Program). JSON options move to DI (`ConfigureHttpJsonOptions`) and `FACTORY_TOKEN` is registered so the ingest endpoint reads it from DI instead of a captured closure. With ticket 03 done, the ingest endpoint collapses to ~5 lines: auth check, deserialize, `store.Apply(runId, ev)`, return 202.

**Blocked by:** 02 — Extract schema migration; 03 — Fold write-side into IRunStore.Apply.

**Status:** ready-for-agent

- [ ] `Runs/RunsEndpoints.cs` exposes `MapRunsEndpoints(this WebApplication)` mapping the run routes; `Program.cs` calls it.
- [ ] JSON serializer options registered via `ConfigureHttpJsonOptions`; no endpoint captures a JSON-opts closure variable.
- [ ] `FACTORY_TOKEN` available through DI/config to the ingest endpoint; the `X-Factory-Token` auth check behavior is unchanged (401 on missing/mismatch).
- [ ] The ingest endpoint body is ~5 lines (auth, deserialize, `store.Apply`, 202).
- [ ] `Program.cs` contains only bootstrap: config fail-fast validation, DI registration, `RunsSchema.EnsureAsync`, `MapRunsEndpoints`, static-file/SPA wiring, `Run`.
- [ ] `dotnet test dashboard/src/Api.Tests` stays green.
