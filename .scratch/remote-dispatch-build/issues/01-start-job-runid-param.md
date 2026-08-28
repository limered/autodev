# 01 — start-job.ps1 accepts an injected RunId

**What to build:** `start-job.ps1` can be told which run id to use, instead of always generating its own. When no id is supplied it behaves exactly as today (generates a fresh GUID); when the dispatch client supplies one (later tickets), that id is the run's identity throughout its lifecycle. This is the prefactor that lets a queued web item correlate to its resulting run.

**Blocked by:** None — can start immediately.

**Status:** ready-for-agent

- [ ] `start-job.ps1` accepts an optional `-RunId` parameter that defaults to a newly generated GUID.
- [ ] The supplied (or defaulted) run id flows to every lifecycle event and to `watch-heartbeat.ps1`, exactly where the self-generated id flows today.
- [ ] Running `start-job.ps1` **without** `-RunId` produces identical behaviour to before (the runs row uses a fresh GUID).
- [ ] Running with `-RunId <guid>` results in a runs row keyed by that exact id.
- [ ] Local flow unchanged: `start-job.ps1` and `start-issue.ps1` remain hand-runnable with no new required arguments.
