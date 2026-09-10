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
  // derives each stage's status from the seeded stages plus currentCategory.
  function stageStatusClass(status) {
    if (status === "done") return "stage-done";
    if (status === "running") return "stage-running";
    if (status === "failed") return "stage-failed";
    return "stage-pending";
  }

  function formatTokenCount(n) {
    return n >= 1000 ? `${(n / 1000).toFixed(1)}k` : String(n);
  }

  function formatDuration(ms) {
    const s = Math.round((ms ?? 0) / 1000);
    if (s < 60) return `${s}s`;
    return `${Math.floor(s / 60)}m ${String(s % 60).padStart(2, "0")}s`;
  }

  const secs = secondsSince(run.lastHeartbeatAt, nowMs);
  // Category identity rides beside the worker name on every stage; runs
  // persisted before categories carry no category and fall back to the worker
  // name, so they render exactly as today.
  const stages = (run.stages || []).map((s) => ({
    agent: s.agent,
    category: s.category ?? s.agent,
    model: s.model,
    statusClass: stageStatusClass(s.status),
  }));

  const rawSteps = Array.isArray(run.steps) ? run.steps : [];
  const agentCounts = {};
  for (const s of rawSteps) {
    agentCounts[s.agent] = (agentCounts[s.agent] ?? 0) + 1;
  }
  // ponytail: only quality-loop participants indent; test-runner/1 is an outer re-run keyed apart from test-runner/0
  const LOOP_AGENTS = new Set(["static-analysis", "feature-builder"]);
  const steps = rawSteps.map((s) => {
    const input = s.inputTokens ?? 0;
    const output = s.outputTokens ?? 0;
    const total = input + output;
    const duration = s.durationMs ?? 0;
    const iteration = s.iteration ?? 0;
    const isLoop = iteration !== 0 && LOOP_AGENTS.has(s.agent);
    return {
      agent: s.agent,
      iteration,
      model: s.model ?? "—",
      statusClass: stageStatusClass(s.status),
      isLoop,
      showIteration: iteration !== 0 && (agentCounts[s.agent] ?? 0) > 1,
      inputTokens: input,
      outputTokens: output,
      durationMs: duration,
      cost: s.cost ?? null,
      tokensLabel: formatTokenCount(total),
      durationLabel: formatDuration(duration),
      stats:
        total === 0 && duration === 0
          ? "—"
          : `${formatTokenCount(total)} · ${formatDuration(duration)}`,
    };
  });

  const devLoop =
    steps.length > 0
      ? steps
      : stages.map((s) => ({
          agent: s.agent,
          category: s.category,
          iteration: 0,
          model: s.model,
          statusClass: s.statusClass,
          isLoop: false,
          showIteration: false,
          inputTokens: 0,
          outputTokens: 0,
          durationMs: 0,
          cost: null,
          tokensLabel: "—",
          durationLabel: "0s",
          stats: "—",
        }));
  const devLoopTokens = devLoop.reduce((n, s) => n + s.inputTokens + s.outputTokens, 0);
  const devLoopMs = devLoop.reduce((n, s) => n + s.durationMs, 0);

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
    steps,
    hasSteps: steps.length > 0,
    devLoop,
    hasDevLoop: devLoop.length > 0,
    devLoopTotals: {
      tokens: devLoopTokens,
      ms: devLoopMs,
      tokensLabel: formatTokenCount(devLoopTokens),
      durationLabel: formatDuration(devLoopMs),
    },
  };
}
