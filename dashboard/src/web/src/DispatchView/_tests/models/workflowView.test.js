import { describe, it, expect } from "vitest";
import { rowCatalogSummary, workflowRowState } from "../../models/workflowView.js";

const factoryCatalog = {
  defaultWorkflow: "full",
  workflows: [
    { name: "full", stageCount: 5 },
    { name: "quick", stageCount: 2 },
  ],
};

const targetEntry = (content) => ({
  repo: "owner/repo",
  source: "target",
  sha: "sha-1",
  content,
  fetchedAt: "2026-01-01T00:00:00Z",
});

const namedWorkflowsJson = JSON.stringify({
  stages: { implementation: {}, ci: {} },
  workflows: { full: ["implementation", "ci"], quick: ["implementation"] },
  defaultWorkflow: "full",
});

const legacyStagesJson = JSON.stringify({ stages: { a: {}, b: {} } });

const unclaimed = { runId: null };
const claimed = { runId: "550e8400-e29b-41d4-a716-446655440000" };

describe("rowCatalogSummary", () => {
  it("a target repo file fully replaces the factory catalog", () => {
    const summary = rowCatalogSummary(targetEntry(namedWorkflowsJson), factoryCatalog);

    expect(summary).toEqual({
      usable: true,
      workflows: [
        { name: "full", stageCount: 2 },
        { name: "quick", stageCount: 1 },
      ],
      defaultWorkflow: "full",
    });
  });

  it("a legacy stages-only target file reads as the default workflow", () => {
    const summary = rowCatalogSummary(targetEntry(legacyStagesJson), factoryCatalog);

    expect(summary).toEqual({
      usable: true,
      workflows: [{ name: "default", stageCount: 2 }],
      defaultWorkflow: "default",
    });
  });

  it("an unreadable target file falls back to the factory catalog summary", () => {
    const summary = rowCatalogSummary(targetEntry("{not json"), factoryCatalog);

    expect(summary).toEqual({
      usable: true,
      workflows: factoryCatalog.workflows,
      defaultWorkflow: "full",
    });
  });

  it("an unreadable target file with no factory read names the unresolved default", () => {
    const summary = rowCatalogSummary(targetEntry("{not json"), null);

    expect(summary).toEqual({ usable: false, workflows: [], defaultWorkflow: "default" });
  });

  it("a fallback source or no row entry reads the factory feed", () => {
    expect(
      rowCatalogSummary({ source: "factory-fallback", content: null }, factoryCatalog),
    ).toEqual({
      usable: true,
      workflows: factoryCatalog.workflows,
      defaultWorkflow: "full",
    });
    expect(rowCatalogSummary(null, factoryCatalog)).toEqual({
      usable: true,
      workflows: factoryCatalog.workflows,
      defaultWorkflow: "full",
    });
  });

  it("no factory catalog at all resolves unusable naming the legacy default", () => {
    expect(rowCatalogSummary(null, null)).toEqual({
      usable: false,
      workflows: [],
      defaultWorkflow: "default",
    });
  });
});

describe("workflowRowState", () => {
  const summary = rowCatalogSummary(null, factoryCatalog);

  it("resolves to the picked workflow while unclaimed", () => {
    expect(workflowRowState(summary, unclaimed, "quick")).toEqual({
      kind: "ready",
      name: "quick",
      stageCount: 2,
    });
  });

  it("resolves to the catalog default with no pick", () => {
    expect(workflowRowState(summary, unclaimed, null)).toEqual({
      kind: "ready",
      name: "full",
      stageCount: 5,
    });
  });

  it("freezes the active pick once the row is claimed", () => {
    expect(workflowRowState(summary, claimed, "quick")).toEqual({
      kind: "frozen",
      name: "quick",
      stageCount: 2,
    });
  });

  it("freezes the catalog default on claimed rows with no pick", () => {
    expect(workflowRowState(summary, claimed, null)).toEqual({
      kind: "frozen",
      name: "full",
      stageCount: 5,
    });
  });

  it("keeps claimed rows on frozen text even when the pick vanished", () => {
    expect(workflowRowState(summary, claimed, "retired-flow")).toEqual({
      kind: "frozen",
      name: "full",
      stageCount: 5,
    });
  });

  it("shows the muted default notice when no catalog is readable", () => {
    const missing = { usable: false, workflows: [], defaultWorkflow: "full" };

    expect(workflowRowState(missing, unclaimed, null)).toEqual({
      kind: "missing",
      name: "full",
      stageCount: 0,
    });
  });

  it("shows the red vanished warning naming the stale pick and the reset default", () => {
    expect(workflowRowState(summary, unclaimed, "retired-flow")).toEqual({
      kind: "vanished",
      stalePick: "retired-flow",
      hasDefault: true,
      name: "full",
      stageCount: 5,
    });
  });

  it("marks hasDefault false — no arbitrary first-workflow reset fallback", () => {
    const noDefault = {
      usable: true,
      workflows: [{ name: "only", stageCount: 3 }],
      defaultWorkflow: "gone",
    };

    expect(workflowRowState(noDefault, unclaimed, "retired-flow")).toEqual({
      kind: "vanished",
      stalePick: "retired-flow",
      hasDefault: false,
      name: "gone",
      stageCount: 0,
    });
  });
});

// Contract fixture pinning parse parity with FactoryCatalog.ParseContent
// (Api/Catalogs/FactoryCatalog.cs, FactoryCatalogTests Read_InvalidCatalog...):
// every catalog C# rejects answers the factory fallback here too — never a
// partial JS-only parse the claim-path validator would reject.
describe("rowCatalogSummary parity with FactoryCatalog.ParseContent", () => {
  const rejected = (json) =>
    expect(rowCatalogSummary(targetEntry(json), factoryCatalog)).toEqual({
      usable: true,
      workflows: factoryCatalog.workflows,
      defaultWorkflow: "full",
    });

  it.each([
    ["an empty workflows map", '{"stages":{"a":{}},"workflows":{},"defaultWorkflow":"x"}'],
    [
      "an empty stage list",
      '{"stages":{"a":{}},"workflows":{"empty":[]},"defaultWorkflow":"empty"}',
    ],
    [
      "an unknown stage id",
      '{"stages":{"a":{}},"workflows":{"bad":["a","ghost"]},"defaultWorkflow":"bad"}',
    ],
    [
      "a duplicate stage id",
      '{"stages":{"a":{}},"workflows":{"dup":["a","a"]},"defaultWorkflow":"dup"}',
    ],
    [
      "the reserved default name",
      '{"stages":{"a":{}},"workflows":{"default":["a"]},"defaultWorkflow":"default"}',
    ],
    ["a missing default marker", '{"stages":{"a":{}},"workflows":{"only":["a"]}}'],
    [
      "an unknown default marker",
      '{"stages":{"a":{}},"workflows":{"only":["a"]},"defaultWorkflow":"ghost"}',
    ],
  ])("rejects %s", (_, json) => rejected(json));

  it("rejects a non-array workflow value where C# throws", () => {
    rejected('{"stages":{"a":{}},"workflows":{"bad":5},"defaultWorkflow":"bad"}');
  });

  it("rejects a workflows value that is not a map", () => {
    rejected('{"stages":{"a":{}},"workflows":[{"name":"only","stages":["a"]}]}');
  });

  it("rejects a stages value that is not a map", () => {
    rejected('{"stages":["a"],"workflows":{"only":["a"]},"defaultWorkflow":"only"}');
  });

  it("rejects a doc that is not an object", () => {
    rejected('["stages"]');
  });
});
