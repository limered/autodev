# 07 — Detect a stalled run and fail the job

**What to build:** The host stops blocking forever on a stalled agent. The job run is launched non-blocking and the host polls the heartbeat marker's freshness inside the VM; when the marker has been stale for more than 5 minutes, the job is failed through the existing teardown path instead of hanging. The poll/detection logic lives in a sibling script that `start-job.ps1` calls, keeping its `try/finally` uncluttered.

**Blocked by:** 06 — Emit heartbeat from the job run

**Status:** done

Implemented by the factory as a VM job — PR #5: https://github.com/limered/autodev/pull/5 (adds `watch-heartbeat.ps1`, rewires `start-job.ps1` to run the job as a background PS job with concurrent heartbeat polling).

- [x] The job run is launched so the host is not blocked on a single synchronous exec for its entire duration.
- [x] The host polls the marker's freshness via `multipass exec` on an interval.
- [x] A marker stale for more than 5 minutes fails the job through the existing failure/teardown path (reuses `$jobFailed` / `finally`).
- [x] A healthy job that produces output at least every 5 minutes runs to completion and is not false-killed.
- [x] A wedged run (e.g. a dead model) is detected and killed at ~5 minutes rather than hanging indefinitely.
