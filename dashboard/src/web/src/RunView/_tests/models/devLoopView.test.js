import { describe, it, expect } from "vitest";
import { devLoopView } from "../../models/devLoopView.js";

describe("devLoopView", () => {
  const baseRun = {
    runId: "run-1",
    repo: "owner/repo",
    branch: "main",
    model: "test-model",
    status: "running",
  };

  describe("stages", () => {
    const stages = [
      { agent: "feature-builder", model: "m1", status: "done" },
      { agent: "test-runner", model: "m2", status: "running" },
      { agent: "pr-author", model: "m3", status: "pending" },
    ];

    it("maps each stage status to a design-A color class", () => {
      const run = { ...baseRun, stages };

      const view = devLoopView(run);

      expect(view.stages).toHaveLength(3);
      expect(view.stages[0]).toEqual({
        agent: "feature-builder",
        category: "uncategorized",
        model: "m1",
        statusClass: "stage-done",
      });
      expect(view.stages[1]).toEqual({
        agent: "test-runner",
        category: "uncategorized",
        model: "m2",
        statusClass: "stage-running",
      });
      expect(view.stages[2]).toEqual({
        agent: "pr-author",
        category: "uncategorized",
        model: "m3",
        statusClass: "stage-pending",
      });
    });

    it("carries the seeded category beside the worker name", () => {
      const run = {
        ...baseRun,
        stages: [
          { agent: "quality-loop", category: "quality-loop", model: "m2", status: "running" },
        ],
      };

      const view = devLoopView(run);

      expect(view.stages[0]).toEqual({
        agent: "quality-loop",
        category: "quality-loop",
        model: "m2",
        statusClass: "stage-running",
      });
    });

    it("lands a stage with no category in the uncategorized bucket", () => {
      const run = {
        ...baseRun,
        stages: [{ agent: "quality-loop", model: "m2", status: "running" }],
      };

      const view = devLoopView(run);

      expect(view.stages[0].category).toBe("uncategorized");
      expect(view.stages[0].statusClass).toBe("stage-running");
    });

    it("defaults a stage with no status to pending", () => {
      const run = { ...baseRun, stages: [{ agent: "feature-builder", model: "m1" }] };

      const view = devLoopView(run);

      expect(view.stages[0].statusClass).toBe("stage-pending");
    });

    it("returns an empty stage list when the run has no stages", () => {
      const view = devLoopView(baseRun);

      expect(view.stages).toEqual([]);
    });
  });

  describe("steps", () => {
    const step = (overrides = {}) => ({
      agent: "feature-builder",
      iteration: 0,
      model: "m1",
      status: "done",
      inputTokens: 100,
      outputTokens: 50,
      durationMs: 61000,
      cost: 0.0123,
      ...overrides,
    });

    it("maps the flat steps list with tokens and duration", () => {
      const run = {
        ...baseRun,
        steps: [step(), step({ agent: "test-runner", iteration: 0, model: "m2" })],
      };

      const view = devLoopView(run);

      expect(view.hasSteps).toBe(true);
      expect(view.steps).toHaveLength(2);
      expect(view.steps[0]).toMatchObject({
        agent: "feature-builder",
        iteration: 0,
        model: "m1",
        statusClass: "stage-done",
        isLoop: false,
        showIteration: false,
        inputTokens: 100,
        outputTokens: 50,
        durationMs: 61000,
        cost: 0.0123,
      });
      expect(view.steps[0].stats).toBe("150 · 1m 01s");
    });

    it("marks loop rows by iteration and badges only repeated agents", () => {
      const run = {
        ...baseRun,
        steps: [
          step({ agent: "feature-builder", iteration: 0 }),
          step({ agent: "static-analysis", iteration: 1, model: "m2" }),
          step({ agent: "feature-builder", iteration: 1, model: "m1" }),
        ],
      };

      const view = devLoopView(run);

      expect(view.steps[0].isLoop).toBe(false);
      expect(view.steps[0].showIteration).toBe(false);
      expect(view.steps[1].isLoop).toBe(true);
      expect(view.steps[1].showIteration).toBe(false);
      expect(view.steps[2].isLoop).toBe(true);
      expect(view.steps[2].showIteration).toBe(true);
    });

    it("keeps the phase-4 test re-run as a numbered outer step", () => {
      const run = {
        ...baseRun,
        steps: [
          step({ agent: "test-runner", iteration: 0 }),
          step({ agent: "test-runner", iteration: 1 }),
        ],
      };

      const view = devLoopView(run);

      expect(view.steps[1].isLoop).toBe(false);
      expect(view.steps[1].showIteration).toBe(true);
    });

    it("maps failed steps to the failed colour class", () => {
      const run = { ...baseRun, steps: [step({ status: "failed" })] };

      expect(devLoopView(run).steps[0].statusClass).toBe("stage-failed");
    });

    it("falls back to seeded stages when steps are empty", () => {
      const run = {
        ...baseRun,
        stages: [{ agent: "feature-builder", model: "m1", status: "done" }],
        steps: [],
      };

      const view = devLoopView(run);

      expect(view.hasSteps).toBe(false);
      expect(view.hasDevLoop).toBe(true);
      expect(view.devLoop).toHaveLength(1);
      expect(view.devLoop[0]).toMatchObject({
        agent: "feature-builder",
        category: "uncategorized",
        model: "m1",
        statusClass: "stage-done",
        isLoop: false,
        showIteration: false,
        stats: "—",
      });
    });

    it("carries the category into the seeded fallback rows", () => {
      const run = {
        ...baseRun,
        stages: [
          { agent: "quality-loop", category: "quality-loop", model: "m2", status: "running" },
        ],
        steps: [],
      };

      const view = devLoopView(run);

      expect(view.devLoop[0]).toMatchObject({
        agent: "quality-loop",
        category: "quality-loop",
        statusClass: "stage-running",
      });
    });

    it("prefers steps over stages for the detail rows", () => {
      const run = {
        ...baseRun,
        stages: [{ agent: "feature-builder", model: "m1", status: "done" }],
        steps: [step({ agent: "test-runner", model: "m2" })],
      };

      const view = devLoopView(run);

      expect(view.devLoop).toHaveLength(1);
      expect(view.devLoop[0].agent).toBe("test-runner");
    });

    it("totals tokens and duration across the detail rows", () => {
      const run = {
        ...baseRun,
        steps: [
          step({ inputTokens: 1000, outputTokens: 500, durationMs: 60000 }),
          step({ agent: "test-runner", inputTokens: 2000, outputTokens: 0, durationMs: 30000 }),
        ],
      };

      const view = devLoopView(run);

      expect(view.devLoopTotals.tokens).toBe(3500);
      expect(view.devLoopTotals.ms).toBe(90000);
      expect(view.devLoopTotals.tokensLabel).toBe("3.5k");
      expect(view.devLoopTotals.durationLabel).toBe("1m 30s");
    });

    it("reports no detail rows when neither steps nor stages exist", () => {
      const view = devLoopView(baseRun);

      expect(view.hasSteps).toBe(false);
      expect(view.hasDevLoop).toBe(false);
      expect(view.devLoop).toEqual([]);
    });

    it("reports zero progress when neither steps nor stages exist", () => {
      const view = devLoopView(baseRun);

      expect(view.devLoopProgress).toEqual({ done: 0, total: 0, pct: 0 });
    });
  });

  describe("dev-loop progress", () => {
    const step = (overrides = {}) => ({
      agent: "feature-builder",
      iteration: 0,
      model: "m1",
      status: "done",
      inputTokens: 100,
      outputTokens: 50,
      durationMs: 1000,
      cost: 0.0123,
      ...overrides,
    });

    it("reports full progress when every step is done", () => {
      const run = { ...baseRun, steps: [step(), step({ agent: "test-runner" })] };

      expect(devLoopView(run).devLoopProgress).toEqual({ done: 2, total: 2, pct: 100 });
    });

    it("reports partial progress for a mix of done and running steps", () => {
      const run = {
        ...baseRun,
        steps: [step(), step({ agent: "test-runner", status: "running" })],
      };

      expect(devLoopView(run).devLoopProgress).toEqual({ done: 1, total: 2, pct: 50 });
    });

    it("counts failed steps as terminal", () => {
      const run = {
        ...baseRun,
        steps: [step({ status: "failed" }), step({ agent: "test-runner", status: "running" })],
      };

      expect(devLoopView(run).devLoopProgress).toEqual({ done: 1, total: 2, pct: 50 });
    });

    it("reports zero progress while every step is still running", () => {
      const run = {
        ...baseRun,
        steps: [step({ status: "running" }), step({ agent: "test-runner", status: "pending" })],
      };

      expect(devLoopView(run).devLoopProgress).toEqual({ done: 0, total: 2, pct: 0 });
    });

    it("derives progress from the stages fallback when steps are empty", () => {
      const run = {
        ...baseRun,
        stages: [{ agent: "feature-builder", model: "m1", status: "done" }],
        steps: [],
      };

      expect(devLoopView(run).devLoopProgress).toEqual({ done: 1, total: 1, pct: 100 });
    });
  });

  describe("grouped detail (categories)", () => {
    const categorizedStages = [
      { agent: "implementation", category: "implementation", model: "m1", status: "running" },
      { agent: "quality-loop", category: "quality-loop", model: "m2", status: "pending" },
      { agent: "test-rerun", category: "test-rerun", model: "m1", status: "pending" },
    ];

    const step = (overrides = {}) => ({
      agent: "feature-builder",
      category: "implementation",
      iteration: 0,
      model: "m1",
      status: "done",
      inputTokens: 100,
      outputTokens: 50,
      durationMs: 61000,
      cost: 0.0123,
      ...overrides,
    });

    it("shows category dots only while the run is live: one header per stage, no rows", () => {
      const run = { ...baseRun, status: "running", stages: categorizedStages, steps: [] };

      const view = devLoopView(run);

      expect(view.hasSteps).toBe(false);
      expect(view.hasDevGroups).toBe(true);
      expect(view.devGroups).toHaveLength(3);
      expect(view.devGroups.map((g) => g.category)).toEqual([
        "implementation",
        "quality-loop",
        "test-rerun",
      ]);
      for (const g of view.devGroups) expect(g.steps).toEqual([]);
      expect(view.devGroups[0]).toMatchObject({
        model: "m1",
        statusClass: "stage-running",
      });
      expect(view.devGroups[1]).toMatchObject({
        model: "m2",
        statusClass: "stage-pending",
      });
    });

    it("groups finished per-worker rows under their category header in stage order", () => {
      const run = {
        ...baseRun,
        status: "done",
        stages: categorizedStages.map((s) => ({ ...s, status: "done" })),
        steps: [
          step(),
          step({ agent: "test-runner", iteration: 0 }),
          step({ agent: "static-analysis", category: "quality-loop", iteration: 1, model: "m2" }),
          step({ agent: "feature-builder", category: "quality-loop", iteration: 1 }),
          step({ agent: "test-runner", category: "test-rerun", iteration: 1 }),
        ],
      };

      const view = devLoopView(run);

      expect(view.devGroups).toHaveLength(3);
      expect(view.devGroups[0].category).toBe("implementation");
      expect(view.devGroups[0].steps.map((s) => `${s.agent}/${s.iteration}`)).toEqual([
        "feature-builder/0",
        "test-runner/0",
      ]);
      expect(view.devGroups[1].category).toBe("quality-loop");
      expect(view.devGroups[1].steps.map((s) => `${s.agent}/${s.iteration}`)).toEqual([
        "static-analysis/1",
        "feature-builder/1",
      ]);
      expect(view.devGroups[2].steps.map((s) => `${s.agent}/${s.iteration}`)).toEqual([
        "test-runner/1",
      ]);
    });

    it("keeps the loop affordance and detail granularity on grouped rows", () => {
      const run = {
        ...baseRun,
        status: "done",
        stages: categorizedStages.map((s) => ({ ...s, status: "done" })),
        steps: [
          step(),
          step({ agent: "static-analysis", category: "quality-loop", iteration: 1, model: "m2" }),
          step({ agent: "feature-builder", category: "quality-loop", iteration: 1 }),
        ],
      };

      const view = devLoopView(run);
      const loop = view.devGroups[1];

      expect(loop.steps[0]).toMatchObject({ isLoop: true, showIteration: false });
      expect(loop.steps[1]).toMatchObject({ isLoop: true, showIteration: true });
      expect(loop.steps[1]).toMatchObject({
        inputTokens: 100,
        outputTokens: 50,
        durationMs: 61000,
        cost: 0.0123,
        stats: "150 · 1m 01s",
      });
    });

    it("totals each group without changing the overall toggle totals", () => {
      const run = {
        ...baseRun,
        status: "done",
        stages: categorizedStages.map((s) => ({ ...s, status: "done" })),
        steps: [
          step({ inputTokens: 1000, outputTokens: 500, durationMs: 60000 }),
          step({
            agent: "static-analysis",
            category: "quality-loop",
            iteration: 1,
            inputTokens: 2000,
            outputTokens: 0,
            durationMs: 30000,
          }),
        ],
      };

      const view = devLoopView(run);

      expect(view.devGroups[0].totals).toMatchObject({
        tokens: 1500,
        ms: 60000,
        tokensLabel: "1.5k",
        durationLabel: "1m 00s",
      });
      expect(view.devGroups[1].totals).toMatchObject({ tokens: 2000, ms: 30000 });
      expect(view.devLoopTotals).toMatchObject({ tokens: 3500, ms: 90000 });
    });

    it("marks a group failed when any member failed, done when all are done", () => {
      const failed = {
        ...baseRun,
        status: "failed",
        stages: categorizedStages,
        steps: [
          step(),
          step({
            agent: "static-analysis",
            category: "quality-loop",
            iteration: 1,
            status: "failed",
          }),
        ],
      };

      const view = devLoopView(failed);

      expect(view.devGroups[0].statusClass).toBe("stage-done");
      expect(view.devGroups[1].statusClass).toBe("stage-failed");
    });

    it("groups steps without a category under the uncategorized bucket", () => {
      const run = {
        ...baseRun,
        status: "done",
        stages: [{ agent: "feature-builder", model: "m1", status: "done" }],
        steps: [{ ...step(), category: undefined }],
      };

      const view = devLoopView(run);

      expect(view.devGroups).toHaveLength(1);
      expect(view.devGroups[0].category).toBe("uncategorized");
      expect(view.devGroups[0].steps).toHaveLength(1);
    });

    it("keeps staged groups intact when an unmapped worker lands in uncategorized", () => {
      const run = {
        ...baseRun,
        status: "done",
        stages: [
          { agent: "implementation", category: "implementation", model: "m1", status: "done" },
        ],
        steps: [step(), step({ agent: "ghost", category: "uncategorized" })],
      };

      const view = devLoopView(run);

      expect(view.devGroups.map((g) => g.category)).toEqual(["implementation", "uncategorized"]);
      expect(view.devGroups[0].steps).toHaveLength(1);
      expect(view.devGroups[0].statusClass).toBe("stage-done");
      expect(view.devGroups[1].steps.map((s) => s.agent)).toEqual(["ghost"]);
    });

    it("appends rows whose category matches no stage after the staged groups", () => {
      const run = {
        ...baseRun,
        status: "done",
        stages: [
          { agent: "implementation", category: "implementation", model: "m1", status: "done" },
        ],
        steps: [step(), step({ agent: "ghost", category: "ghost" })],
      };

      const view = devLoopView(run);

      expect(view.devGroups.map((g) => g.category)).toEqual(["implementation", "ghost"]);
      expect(view.devGroups[1]).toMatchObject({ model: "—", statusClass: "stage-done" });
    });

    it("reports no groups when neither steps nor stages exist", () => {
      const view = devLoopView(baseRun);

      expect(view.hasDevGroups).toBe(false);
      expect(view.devGroups).toEqual([]);
    });
  });
});
