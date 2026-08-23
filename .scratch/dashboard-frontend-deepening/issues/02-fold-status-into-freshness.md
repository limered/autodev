# 02 — Fold status into freshness

**What to build:** Freshness stops being computed from heartbeat age alone. It takes the backend `status` as an input so the backend's verdict wins: terminal runs (`done`, `failed`) always read as settled, and warn/danger staleness only applies while a run is live. The frontend and backend no longer give two different answers for the "last seen" cell — the `stalled` status the backend already folds is honoured rather than re-derived.

**Blocked by:** 01 — Deepen the Run view-model module (freshness rides that same interface).

**Status:** ready-for-agent

- [ ] Freshness is derived from `status` plus heartbeat age within the run view-model
- [ ] Terminal statuses render as settled regardless of heartbeat age
- [ ] Warn/danger staleness only appears for non-terminal, live runs
- [ ] Tests cover the status/age combinations, including a stale-but-terminal run and a fresh-but-stalled run
