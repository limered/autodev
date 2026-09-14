import { secondsSince, lastSeenLabel } from "../../_shared/models/time.js";
import { isTerminalRun } from "./runStatus.js";

export const UNCATEGORIZED = "uncategorized";

// Freshness is decided here so thresholds, terminal handling and the
// missing-heartbeat case evolve together.
export function freshnessFor(status, secs, terminal) {
  if (secs === null) return "unknown";
  if (terminal) return "settled";
  if (status === "stalled") return "stale-warn";
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
  const stages = (run.stages || []).map((s) => ({
    agent: s.agent,
    category: s.category ?? UNCATEGORIZED,
    model: s.model,
    statusClass: stageStatusClass(s.status),
  }));

  const rawSteps = Array.isArray(run.steps) ? run.steps : [];
  const agentCounts = {};
  for (const s of rawSteps) {
    agentCounts[s.agent] = (agentCounts[s.agent] ?? 0) + 1;
  }
  // A loop slot carries several loop passes while a sequential re-run owns
  // its slot alone, so loop-ness follows the backend category assignment.
  const loopPassesByCategory = new Map();
  for (const s of rawSteps) {
    if ((s.iteration ?? 0) === 0) continue;
    const key = s.category || UNCATEGORIZED;
    loopPassesByCategory.set(key, (loopPassesByCategory.get(key) ?? 0) + 1);
  }
  const steps = rawSteps.map((s) => {
    const input = s.inputTokens ?? 0;
    const output = s.outputTokens ?? 0;
    const total = input + output;
    const duration = s.durationMs ?? 0;
    const iteration = s.iteration ?? 0;
    const isLoop =
      iteration !== 0 && (loopPassesByCategory.get(s.category || UNCATEGORIZED) ?? 0) > 1;
    return {
      agent: s.agent,
      category: s.category || UNCATEGORIZED,
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
  const devLoopDone = devLoop.filter(
    (s) => s.statusClass === "stage-done" || s.statusClass === "stage-failed",
  ).length;
  const devLoopTotal = devLoop.length;
  const devLoopPct = devLoopTotal === 0 ? 0 : Math.round((devLoopDone / devLoopTotal) * 100);

  // Live runs show category dots only so there is no per-worker churn mid-run.
  function headerStatusFor(rows) {
    if (rows.some((r) => r.statusClass === "stage-failed")) return "stage-failed";
    if (rows.length > 0 && rows.every((r) => r.statusClass === "stage-done")) return "stage-done";
    if (rows.some((r) => r.statusClass === "stage-running")) return "stage-running";
    return "stage-pending";
  }

  const stageByCategory = new Map();
  for (const st of stages) {
    if (!stageByCategory.has(st.category)) stageByCategory.set(st.category, st);
  }
  const devGroups = [];
  const groupByKey = new Map();
  function ensureGroup(key) {
    let g = groupByKey.get(key);
    if (!g) {
      const st = stageByCategory.get(key);
      g = {
        category: key,
        model: st ? st.model : "—",
        statusClass: st ? st.statusClass : "stage-pending",
        steps: [],
        totals: { tokens: 0, ms: 0, tokensLabel: "0", durationLabel: "0s" },
      };
      groupByKey.set(key, g);
      devGroups.push(g);
    }
    return g;
  }
  for (const st of stages) ensureGroup(st.category);
  for (const row of steps) ensureGroup(row.category).steps.push(row);
  for (const g of devGroups) {
    if (g.steps.length === 0) continue;
    const tokens = g.steps.reduce((n, s) => n + s.inputTokens + s.outputTokens, 0);
    const ms = g.steps.reduce((n, s) => n + s.durationMs, 0);
    g.statusClass = headerStatusFor(g.steps);
    g.totals = {
      tokens,
      ms,
      tokensLabel: formatTokenCount(tokens),
      durationLabel: formatDuration(ms),
    };
  }

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
    freshnessClass: freshnessFor(run.status, secs, terminal),
    statusClass: `status-${run.status}`,
    started: formatTime(run.startedAt),
    stages,
    steps,
    hasSteps: steps.length > 0,
    devLoop,
    hasDevLoop: devLoop.length > 0,
    devGroups,
    hasDevGroups: devGroups.length > 0,
    devLoopTotals: {
      tokens: devLoopTokens,
      ms: devLoopMs,
      tokensLabel: formatTokenCount(devLoopTokens),
      durationLabel: formatDuration(devLoopMs),
    },
    devLoopProgress: {
      done: devLoopDone,
      total: devLoopTotal,
      pct: devLoopPct,
    },
  };
}
