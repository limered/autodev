# 03 — Fold write-side into IRunStore.Apply

**What to build:** The write/read asymmetry in the store disappears. Today reads (`All`/`Active`/`Get`) live in `RunStore` while the entire write path (insert-on-start, heartbeat-only update, diff-based optimistic-concurrency UPDATE) lives inline in `Program.cs` as `PersistRun`. Consolidate all run persistence behind one deep method: `IRunStore.Apply(Guid runId, RunEvent ev)` owns fetch-current → `RunFold.Apply` → persist and returns the new `RunState`. `PersistRun` is deleted from `Program.cs`. The ingest endpoint's fetch/fold/persist logic now sits behind a single store call.

**Blocked by:** 01 — Move files into Runs/ feature folder.

**Status:** ready-for-agent

- [ ] `IRunStore` gains `Task<RunState> Apply(Guid runId, RunEvent ev)`; `RunStore` implements it by folding the old `PersistRun` logic (insert-on-start, heartbeat-only monotonic update, diff-based `WHERE @updatedAt > updated_at` update) in.
- [ ] `RunFold.Apply` is called internally by the store; it stays a pure static helper.
- [ ] `PersistRun` no longer exists in `Program.cs`.
- [ ] Fetch-current and persist are one atomic operation (transaction), so concurrent events to the same run cannot interleave — the monotonic guard still applies.
- [ ] A new ingest test drives `Apply` against a fake `IRunStore` (the second adapter), covering start, heartbeat-only, out-of-order no-op, and a normal update. No DB, no framework fixtures.
- [ ] `dotnet test dashboard/src/Api.Tests` stays green.
