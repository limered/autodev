# 04 — Terminal + failure paths

**What to build:** Widen the thread so runs reach a correct final state on the dashboard. A successful run ends `done` with a PR link; a failed run ends `failed` with a reason. Terminal states are protected from late events.

**Blocked by:** 03 — Live run: agent-started + heartbeat.

**Status:** done

- [x] The backend folds `pr-verified` (sets prUrl), `run-finished` (→ `done`, sets finishedAt), and `run-failed` (→ `failed`, sets finishedAt + failureReason).
- [x] Sticky-terminal rule: once a run is `done` or `failed`, no later event can change its status.
- [x] `start-job.ps1` emits `pr-verified` when the PR is found, `run-finished` on success, and `run-failed` in its catch block (with a free-text failureReason).
- [x] The Vue table shows terminal status, the PR link (when present), and the failure reason (when failed).
- [ ] A real successful run ends `done` with its PR link; a real failing run ends `failed` with a reason — both correct on the dashboard. *(verified live after redeploy, as with 02/03.)*
