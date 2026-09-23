export function workflowRowState(catalog, item, pickedName) {
  const claimed = Boolean(item?.runId);
  const entries = catalog?.workflows;
  const usable = Array.isArray(entries) && entries.length > 0;
  if (!usable) {
    return {
      kind: "missing",
      name: catalog?.defaultWorkflow ?? "default",
      stageCount: 0,
    };
  }
  const active = pickedName ?? catalog.defaultWorkflow;
  const found = entries.find((w) => w.name === active);
  if (!found) {
    const fallback = entries.find((w) => w.name === catalog.defaultWorkflow) ?? entries[0];
    return { kind: "missing", name: fallback.name, stageCount: fallback.stageCount };
  }
  if (claimed) return { kind: "frozen", name: found.name, stageCount: found.stageCount };
  return { kind: "ready", name: found.name, stageCount: found.stageCount };
}
