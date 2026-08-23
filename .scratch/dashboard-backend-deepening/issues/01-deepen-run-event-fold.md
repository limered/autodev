# 01 — Deepen the run-event fold

**What to build:** Extract the run-event folding logic out of the `POST /runs/{runId}/events` handler into a deep `RunFold` module with a single interface: `apply(currentState, event) → nextState-or-noop`. The handler keeps HTTP concerns (auth, deserialize, status codes, persistence); the fold owns every transition rule.

**Problem:** The `switch (ev.Type)` in `Program.cs:82` has 8 arms, each opening a connection and running SQL with copy-pasted guard clauses ("timestamp-guarded", "sticky-terminal"). The core invariants — a stall/finish must not revive an already `done`/`failed` run, out-of-order events are no-ops — live scattered across 8 WHERE clauses and are only testable through HTTP + Postgres. The fold has no seam: it's where bugs will hide and the one place currently impossible to test in isolation.

**Solution:** A `RunFold` module maps `(currentStatus, updatedAt, event)` to the resulting field changes (or a no-op). Guards become pure logic. The handler becomes: deserialize → load current run state → `apply` → persist result.

**Blocked by:** none.

**Status:** todo

- [ ] `RunFold.apply` is a pure function of current run state + event → intended change (or no-op), with no DB or HTTP dependency.
- [ ] All 8 event types (`run-started`, `agent-started`, `heartbeat`, `stall-detected`, `freeze-captured`, `pr-verified`, `run-finished`, `run-failed`) route through `apply`; unknown types remain accepted-and-ignored.
- [ ] Sticky-terminal invariant holds: `stall-detected`/`run-finished`/`run-failed` arriving after a terminal status is a no-op.
- [ ] Timestamp-guard invariant holds: an event with `at <= updatedAt` is a no-op for status-changing events; heartbeat guards against its own last value.
- [ ] Tests exercise the fold directly (no DB, no HTTP): terminal-revive rejection, out-of-order rejection, each transition.
- [ ] `POST /runs/{runId}/events` behaviour is unchanged from the outside (same status codes, same auth, same persisted result).
