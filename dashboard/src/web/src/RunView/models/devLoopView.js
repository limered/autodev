export const UNCATEGORIZED = "uncategorized";

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

function normalizeCategory(value) {
  return typeof value === "string" && value.trim() ? value : UNCATEGORIZED;
}

export function devLoopView(run) {
  const stages = (run.stages || []).map((stage) => ({
    agent: stage.agent,
    category: normalizeCategory(stage.category),
    model: stage.model,
    statusClass: stageStatusClass(stage.status),
  }));

  const rawSteps = Array.isArray(run.steps) ? run.steps : [];
  const agentCounts = {};
  for (const rawStep of rawSteps) {
    agentCounts[rawStep.agent] = (agentCounts[rawStep.agent] ?? 0) + 1;
  }
  const LOOP_AGENTS = new Set(["static-analysis", "feature-builder"]);
  const steps = rawSteps.map((rawStep) => {
    const input = rawStep.inputTokens ?? 0;
    const output = rawStep.outputTokens ?? 0;
    const total = input + output;
    const duration = rawStep.durationMs ?? 0;
    const iteration = rawStep.iteration ?? 0;
    const isLoop = iteration !== 0 && LOOP_AGENTS.has(rawStep.agent);
    return {
      agent: rawStep.agent,
      category: normalizeCategory(rawStep.category),
      iteration,
      model: rawStep.model ?? "—",
      statusClass: stageStatusClass(rawStep.status),
      isLoop,
      showIteration: iteration !== 0 && (agentCounts[rawStep.agent] ?? 0) > 1,
      inputTokens: input,
      outputTokens: output,
      durationMs: duration,
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
      : stages.map((stage) => ({
          agent: stage.agent,
          category: stage.category,
          iteration: 0,
          model: stage.model,
          statusClass: stage.statusClass,
          isLoop: false,
          showIteration: false,
          inputTokens: 0,
          outputTokens: 0,
          durationMs: 0,
          tokensLabel: "—",
          durationLabel: "0s",
          stats: "—",
        }));
  const devLoopTokens = devLoop.reduce(
    (sum, step) => sum + step.inputTokens + step.outputTokens,
    0,
  );
  const devLoopMs = devLoop.reduce((sum, step) => sum + step.durationMs, 0);
  const devLoopDone = devLoop.filter(
    (step) => step.statusClass === "stage-done" || step.statusClass === "stage-failed",
  ).length;
  const devLoopTotal = devLoop.length;
  const devLoopPct = devLoopTotal === 0 ? 0 : Math.round((devLoopDone / devLoopTotal) * 100);

  const stageByCategory = new Map();
  for (const stage of stages) {
    if (!stageByCategory.has(stage.category)) stageByCategory.set(stage.category, stage);
  }
  const devGroups = [];
  const groupByKey = new Map();
  function ensureGroup(key) {
    let group = groupByKey.get(key);
    if (!group) {
      const stage = stageByCategory.get(key);
      group = {
        category: key,
        model: stage ? stage.model : "—",
        statusClass: stage ? stage.statusClass : "stage-pending",
        steps: [],
        totals: { tokens: 0, ms: 0, tokensLabel: "0", durationLabel: "0s" },
      };
      groupByKey.set(key, group);
      devGroups.push(group);
    }
    return group;
  }
  for (const stage of stages) ensureGroup(stage.category);
  for (const row of steps) {
    const key = stageByCategory.has(row.category) ? row.category : UNCATEGORIZED;
    ensureGroup(key).steps.push(row);
  }
  for (const group of devGroups) {
    if (group.steps.length === 0) continue;
    const tokens = group.steps.reduce((sum, step) => sum + step.inputTokens + step.outputTokens, 0);
    const ms = group.steps.reduce((sum, step) => sum + step.durationMs, 0);
    group.totals = {
      tokens,
      ms,
      tokensLabel: formatTokenCount(tokens),
      durationLabel: formatDuration(ms),
    };
  }

  return {
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
