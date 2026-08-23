# 01 — Deepen the Run view-model module

**What to build:** A single pure `runView(run, nowMs)` function turns a run object into every value the card displays — the "last seen" label, its freshness class, the status class, and the "started" label. The `App.vue` template reads these fields off the view-model instead of calling loose helper functions, and `now` is passed in as a plain millisecond number rather than read from Vue reactivity inside the helpers. The first frontend test exercises the whole card's display logic through this one interface.

**Blocked by:** None — can start immediately.

**Status:** ready-for-agent

- [ ] `runView(run, nowMs)` returns last-seen label, freshness class, status class, and started label for a run
- [ ] The five inline helpers (`secondsSince`, `lastSeen`, `freshnessClass`, `formatTime`, `statusClass`) no longer exist as separate template-facing functions
- [ ] The template renders identical output to today for launching/running/stalled/done/failed runs and for missing/absent timestamps
- [ ] `now` reaches the derived logic as an injected number, not via reactive access inside the logic
- [ ] A test runner is wired up and one test drives `runView` across representative runs, including null `lastHeartbeatAt`
