// One catalog summary per queue row: the row's own repo catalog from
// /catalogs/{repo} fully replaces the factory catalog when it is a readable
// target file; anything else (absent row, fallback source, unreadable content)
// falls back to the factory feed's workflows — and with no factory read either,
// to a muted unresolved-default state. Pure functions; callers hand in the
// fetched shapes and picks and render whatever states come back.
export function rowCatalogSummary(rowEntry, factoryCatalog) {
  if (rowEntry?.source === "target" && rowEntry.content != null) {
    let doc;
    try {
      doc = JSON.parse(rowEntry.content);
    } catch {
      doc = null;
    }
    const summary = doc && parseWorkflows(doc);
    if (summary) return summary;
    // A target file that cannot be read as a catalog maps to the factory
    // fallback, exactly as the claim path does; the notice names the default
    // that will actually run (the factory one, when the feed has it).
    return factorySummary(factoryCatalog);
  }

  return factorySummary(factoryCatalog);
}

function parseWorkflows(doc) {
  const stageIds = Object.keys(doc?.stages ?? {});
  if (!stageIds.length) return null;

  if (doc.workflows && typeof doc.workflows === "object") {
    const names = Object.keys(doc.workflows);
    if (!names.length) return null;
    const workflows = names.map((name) => ({
      name,
      stageCount: Array.isArray(doc.workflows[name]) ? doc.workflows[name].length : 0,
    }));
    const defaultWorkflow = typeof doc.defaultWorkflow === "string" ? doc.defaultWorkflow : null;
    if (!defaultWorkflow || !workflows.some((w) => w.name === defaultWorkflow)) return null;
    return { usable: true, workflows, defaultWorkflow };
  }

  return {
    usable: true,
    workflows: [{ name: "default", stageCount: stageIds.length }],
    defaultWorkflow: "default",
  };
}

function factorySummary(factoryCatalog) {
  const workflows = Array.isArray(factoryCatalog?.workflows) ? factoryCatalog.workflows : [];
  return {
    usable: workflows.length > 0,
    workflows,
    defaultWorkflow: factoryCatalog?.defaultWorkflow ?? "default",
  };
}

// The row's picker state from one catalog summary plus the row's pick:
// ready while unclaimed, plain frozen text once claimed, a muted default
// notice while no catalog is readable (the factory fallback runs), and a red
// vanished warning naming the stale pick with the reset target when the pick
// names nothing in the catalog. Formal mini-states: kind only, never class names.
export function workflowRowState(summary, item, pickedName) {
  const claimed = Boolean(item?.runId);
  const workflows = summary?.workflows ?? [];
  const defaultWorkflow = summary?.defaultWorkflow ?? "default";
  const find = (name) => workflows.find((w) => w.name === name);

  if (claimed) {
    const found = find(pickedName) ?? find(defaultWorkflow) ?? workflows[0];
    return {
      kind: "frozen",
      name: found?.name ?? defaultWorkflow,
      stageCount: found?.stageCount ?? 0,
    };
  }

  if (!summary?.usable) {
    return { kind: "missing", name: defaultWorkflow, stageCount: 0 };
  }

  if (pickedName && !find(pickedName)) {
    const resetTarget = find(defaultWorkflow) ?? workflows[0];
    return {
      kind: "vanished",
      stalePick: pickedName,
      name: resetTarget.name,
      stageCount: resetTarget.stageCount,
    };
  }

  const found = find(pickedName ?? defaultWorkflow);
  if (!found) {
    return { kind: "missing", name: defaultWorkflow, stageCount: 0 };
  }
  return { kind: "ready", name: found.name, stageCount: found.stageCount };
}
