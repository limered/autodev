import { secondsSince, lastSeenLabel } from "../../_shared/models/time.js";
import { isTerminalRun } from "./runStatus.js";
import { devLoopView } from "./devLoopView.js";

// Freshness is decided here so thresholds, terminal handling and the
// missing-heartbeat case evolve together.
export function freshnessFor(run, nowMs) {
  const secs = secondsSince(run.lastHeartbeatAt, nowMs);
  if (secs === null) return "unknown";
  if (isTerminalRun(run)) return "settled";
  if (run.status === "stalled") return "stale-warn";
  if (secs > 120) return "stale-danger";
  if (secs > 30) return "stale-warn";
  return "fresh";
}

export function runView(run, nowMs) {
  // Terminal runs show a fixed Completed timestamp so the value stays stable across the clock tick.
  const terminal = isTerminalRun(run);

  function formatTime(ts) {
    if (!ts) return "—";
    return new Date(ts).toLocaleString();
  }

  const secs = secondsSince(run.lastHeartbeatAt, nowMs);

  return {
    // RunCard reads view.* everywhere except run.runId.
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
    freshnessClass: freshnessFor(run, nowMs),
    statusClass: `status-${run.status}`,
    started: formatTime(run.startedAt),
    ...devLoopView(run),
  };
}
