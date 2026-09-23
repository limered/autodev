import { describe, it, expect } from "vitest";
import { workflowRowState } from "../../models/workflowView.js";

const catalog = {
  defaultWorkflow: "full",
  workflows: [
    { name: "full", stageCount: 5 },
    { name: "quick", stageCount: 2 },
  ],
};

const unclaimed = { runId: null };
const claimed = { runId: "550e8400-e29b-41d4-a716-446655440000" };

describe("workflowRowState", () => {
  it("resolves to the catalog default with no local pick", () => {
    expect(workflowRowState(catalog, unclaimed, null)).toEqual({
      kind: "ready",
      name: "full",
      stageCount: 5,
    });
  });

  it("resolves to the locally picked workflow while unclaimed", () => {
    expect(workflowRowState(catalog, unclaimed, "quick")).toEqual({
      kind: "ready",
      name: "quick",
      stageCount: 2,
    });
  });

  it("freezes the active pick once the row is claimed", () => {
    expect(workflowRowState(catalog, claimed, "quick")).toEqual({
      kind: "frozen",
      name: "quick",
      stageCount: 2,
    });
  });

  it("freezes the catalog default on claimed rows with no local pick", () => {
    expect(workflowRowState(catalog, claimed, null)).toEqual({
      kind: "frozen",
      name: "full",
      stageCount: 5,
    });
  });

  it("falls back to the muted default notice when the catalog is missing", () => {
    for (const missing of [null, undefined]) {
      expect(workflowRowState(missing, unclaimed, null)).toEqual({
        kind: "missing",
        name: "default",
        stageCount: 0,
      });
    }
  });

  it("falls back to the muted default notice when the catalog has no workflows", () => {
    expect(workflowRowState({ defaultWorkflow: "full", workflows: [] }, unclaimed, null)).toEqual({
      kind: "missing",
      name: "full",
      stageCount: 0,
    });
  });

  it("falls back to the muted default notice when the pick vanished from the catalog", () => {
    expect(workflowRowState(catalog, unclaimed, "retired-flow")).toEqual({
      kind: "missing",
      name: "full",
      stageCount: 5,
    });
  });
});
