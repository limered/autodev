# 03 — Live run: agent-started + heartbeat

**What to build:** Widen the thread so a *running* job shows live liveness on the dashboard. The run moves to `running` when the agent starts, and a "last seen Ns ago" freshness indicator updates as the agent produces output.

**Blocked by:** 02 — One event, host to screen.

**Status:** done

- [x] The backend folds `agent-started` (→ `running`, sets vmName) and `heartbeat` (updates `lastHeartbeatAt`) events.
- [x] Event folding is timestamp-guarded: a field is only written if the event's `at` is newer than the last-applied value, so stale retries and duplicates are no-ops.
- [x] `start-job.ps1` emits `agent-started` when the agent run begins.
- [x] `watch-heartbeat.ps1` emits a `heartbeat` event **only when the VM marker mtime advanced** since last reported.
- [x] The Vue table shows the run's status and a client-computed "last seen Ns ago" staleness derived from `lastHeartbeatAt`.
- [ ] A running job visibly shows live liveness updates on the dashboard. *(Verify after redeploy — same as ticket 02's live check.)*
