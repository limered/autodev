import { secondsSince, lastSeenLabel } from "../../_shared/models/time.js";
import { isTerminalRun } from "./runStatus.js";

// Single freshness verdict (issue #123): thresholds, terminal handling and the
// missing-heartbeat case evolve here together. Both card readers (active and
// history lists) go through runView, so this is the one seam staleness is
// verified through; time.js stays a formatting primitive behind its own seam.
export function freshnessFor(status, secs, terminal) {
  if (secs === null) return "unknown";
  if (terminal) return "settled";
  if (status === "stalled") return "stale-warn";
  if (secs > 120) return "stale-danger";
  if (secs > 30) return "stale-warn";
  return "fresh";
}

export function runView(run, nowMs) {
  // Terminal runs are no longer "seen": instead of a live relative label that
  // ticks upward forever, they show a fixed Completed timestamp derived from
  // the run's own stored times (finishedAt, falling back to lastHeartbeatAt),
  // so the value is stable across the 1s clock tick. Active runs keep the
  // live "Last seen" behaviour.
  const terminal = isTerminalRun(run);

  function formatTime(ts) {
    if (!ts) return "—";
    return new Date(ts).toLocaleString();
  }

  // Design-A badge strip: pending grey, running green, done blue. The backend
  // derives each stage's status from the seeded stages plus currentPhase.
  function stageStatusClass(status) {
    if (status === "done") return "stage-done";
    if (status === "running") return "stage-running";
    return "stage-pending";
  }

  const secs = secondsSince(run.lastHeartbeatAt, nowMs);
  const stages = (run.stages || []).map((s) => ({
    agent: s.agent,
    model: s.model,
    statusClass: stageStatusClass(s.status),
  }));

  return {
    // Run-to-card seam: pass-through display fields plus show* gates so
    // RunCard.vue reads view.* everywhere except run.runId (delete emit).
    statusText: run.status,
    repo: run.repo,
    branch: run.branch,
    vmName: run.vmName,
    prUrl: run.prUrl,
    failureReason: run.failureReason,
    freezePath: run.freezeLocalPath || "—",
    showVm: Boolean(run.vmName),
    showPr: Boolean(run.prUrl),
    showFailure: Boolean(run.status === "failed" && run.failureReason),
    showFreeze: Boolean(run.freezeCaptured),
    timeLabel: terminal ? "Completed" : "Last seen",
    lastSeen: terminal ? null : lastSeenLabel(secs),
    completed: terminal ? formatTime(run.finishedAt ?? run.lastHeartbeatAt) : null,
    freshnessClass: freshnessFor(run.status, secs, terminal),
    statusClass: `status-${run.status}`,
    started: formatTime(run.startedAt),
    stages,
  };
}
