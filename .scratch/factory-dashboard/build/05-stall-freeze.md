# 05 — Stall + freeze

**What to build:** Complete the thread with the stall/freeze path. When a job stalls, the dashboard shows it pass through `stalled` to `failed`, with a freeze indicator. Adds the live-only read endpoint.

**Blocked by:** 04 — Terminal + failure paths.

**Status:** done

- [x] The backend folds `stall-detected` (→ transient `stalled`, sets failureReason) and `freeze-captured` (sets freezeCaptured + freezeLocalPath); a stalled run resolves to `failed` via the subsequent `run-failed`.
- [x] `watch-heartbeat.ps1` emits `stall-detected` on the stall throw; `start-job.ps1` emits `freeze-captured` after the freeze snapshot is written.
- [x] `GET /runs/active` returns only non-terminal runs (`launching|running|stalled`).
- [x] The Vue table shows a freeze indicator (flag + local host path; not remotely viewable) and reflects the stalled→failed transition.
- [ ] A real stalled job is visible on the dashboard passing through stalled and ending failed, with its freeze flagged. _(verifies live after redeploy — matching tickets 02/03/04)_
