export const UNCATEGORIZED = "uncategorized";

export const LOOP_STAGE_TYPE = "loop";

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

function isLoopStageType(type) {
  return typeof type === "string" && type.trim().toLowerCase() === LOOP_STAGE_TYPE;
}

export function buildDevRow({
  agent,
  category,
  iteration = 0,
  model,
  status,
  inputTokens = 0,
  outputTokens = 0,
  durationMs = 0,
  isLoop = false,
  showIteration = false,
}) {
  const input = inputTokens ?? 0;
  const output = outputTokens ?? 0;
  const total = input + output;
  const duration = durationMs ?? 0;
  return {
    agent,
    category: normalizeCategory(category),
    iteration,
    model: model ?? "—",
    statusClass: stageStatusClass(status),
    isLoop,
    showIteration,
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
}

export function devLoopTotals(rows) {
  const tokens = rows.reduce((sum, row) => sum + row.inputTokens + row.outputTokens, 0);
  const ms = rows.reduce((sum, row) => sum + row.durationMs, 0);
  return {
    tokens,
    ms,
    tokensLabel: formatTokenCount(tokens),
    durationLabel: formatDuration(ms),
  };
}

export function devLoopProgress(rows) {
  const total = rows.length;
  const done = rows.filter(
    (row) => row.statusClass === "stage-done" || row.statusClass === "stage-failed",
  ).length;
  return { done, total, pct: total === 0 ? 0 : Math.round((done / total) * 100) };
}

export function devLoop(run) {
  const stagesInput = run.stages || [];
  const normalizedStages = stagesInput.map((stage) => ({
    agent: stage.agent,
    category: normalizeCategory(stage.category),
    model: stage.model,
    statusClass: stageStatusClass(stage.status),
    type: stage.type ?? null,
  }));

  const typeByCategory = new Map();
  for (const stage of normalizedStages) {
    if (!typeByCategory.has(stage.category)) typeByCategory.set(stage.category, stage.type);
  }

  const rawSteps = Array.isArray(run.steps) ? run.steps : [];
  const agentCounts = {};
  for (const rawStep of rawSteps) {
    agentCounts[rawStep.agent] = (agentCounts[rawStep.agent] ?? 0) + 1;
  }
  const steps = rawSteps.map((rawStep) => {
    const category = normalizeCategory(rawStep.category);
    const iteration = rawStep.iteration ?? 0;
    return buildDevRow({
      agent: rawStep.agent,
      category,
      iteration,
      model: rawStep.model,
      status: rawStep.status,
      inputTokens: rawStep.inputTokens ?? 0,
      outputTokens: rawStep.outputTokens ?? 0,
      durationMs: rawStep.durationMs ?? 0,
      isLoop: iteration !== 0 && isLoopStageType(typeByCategory.get(category)),
      showIteration: iteration !== 0 && (agentCounts[rawStep.agent] ?? 0) > 1,
    });
  });

  const rows =
    steps.length > 0
      ? steps
      : stagesInput.map((stage) =>
          buildDevRow({
            agent: stage.agent,
            category: stage.category,
            iteration: 0,
            model: stage.model,
            status: stage.status,
            inputTokens: 0,
            outputTokens: 0,
            durationMs: 0,
            isLoop: false,
            showIteration: false,
          }),
        );

  const stageByCategory = new Map();
  for (const stage of normalizedStages) {
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
        model: stage ? (stage.model ?? "—") : "—",
        statusClass: stage ? stage.statusClass : "stage-pending",
        steps: [],
        totals: { tokens: 0, ms: 0, tokensLabel: "0", durationLabel: "0s" },
      };
      groupByKey.set(key, group);
      devGroups.push(group);
    }
    return group;
  }
  for (const stage of normalizedStages) ensureGroup(stage.category);
  for (const row of steps) {
    const key = stageByCategory.has(row.category) ? row.category : UNCATEGORIZED;
    ensureGroup(key).steps.push(row);
  }
  for (const group of devGroups) {
    if (group.steps.length === 0) continue;
    group.totals = devLoopTotals(group.steps);
  }

  return {
    devLoop: rows,
    hasDevLoop: rows.length > 0,
    devGroups,
    hasDevGroups: devGroups.length > 0,
    devLoopTotals: devLoopTotals(rows),
    devLoopProgress: devLoopProgress(rows),
  };
}
