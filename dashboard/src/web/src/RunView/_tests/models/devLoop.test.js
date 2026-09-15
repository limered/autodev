import { describe, it, expect } from "vitest";
import {
  devLoop,
  buildDevRow,
  devLoopTotals,
  devLoopProgress,
  UNCATEGORIZED,
} from "../../models/devLoop.js";

describe("devLoop", () => {
  describe("uncategorized contract", () => {
    it("pins the uncategorized literal shared across tiers", () => {
      expect(UNCATEGORIZED).toBe("uncategorized");
    });

    it("groups unmapped workers without lighting a stage", () => {
      const run = {
        runId: "run-1",
        repo: "owner/repo",
        branch: "main",
        model: "test-model",
        status: "done",
        stages: [
          { agent: "implementation", category: "implementation", model: "m1", status: "done" },
        ],
        steps: [
          {
            agent: "ghost",
            category: "ghost",
            iteration: 0,
            model: "m-ghost",
            status: "done",
            inputTokens: 100,
            outputTokens: 50,
            durationMs: 1000,
          },
        ],
      };

      const view = devLoop(run);

      expect(view.devGroups.map((g) => g.category)).toEqual(["implementation", "uncategorized"]);
      expect(view.devGroups[1].steps.map((s) => s.agent)).toEqual(["ghost"]);
      expect(view.devGroups[1]).toMatchObject({ model: "—", statusClass: "stage-pending" });
    });
  });

  const baseRun = {
    runId: "run-1",
    repo: "owner/repo",
    branch: "main",
    model: "test-model",
    status: "running",
  };

  const loopStages = [
    {
      agent: "implementation",
      category: "implementation",
      model: "m1",
      status: "done",
      type: "sequential",
    },
    {
      agent: "quality-loop",
      category: "quality-loop",
      model: "m2",
      status: "running",
      type: "loop",
    },
    {
      agent: "test-rerun",
      category: "test-rerun",
      model: "m1",
      status: "pending",
      type: "sequential",
    },
  ];

  describe("unified row builder", () => {
    it("normalizes a step and a stage fallback through one row module", () => {
      const fromStep = buildDevRow({
        agent: "feature-builder",
        category: "implementation",
        iteration: 0,
        model: "m1",
        status: "done",
        inputTokens: 100,
        outputTokens: 50,
        durationMs: 61000,
        isLoop: false,
        showIteration: false,
      });
      const fromStage = buildDevRow({
        agent: "feature-builder",
        category: "implementation",
        iteration: 0,
        model: "m1",
        status: "done",
        inputTokens: 100,
        outputTokens: 50,
        durationMs: 61000,
        isLoop: false,
        showIteration: false,
      });

      expect(fromStage).toEqual(fromStep);
      expect(fromStep).toMatchObject({
        statusClass: "stage-done",
        tokensLabel: "150",
        durationLabel: "1m 01s",
        stats: "150 · 1m 01s",
      });
    });

    it("totals tokens and duration behind its own seam", () => {
      const rows = [
        buildDevRow({
          agent: "a",
          category: "implementation",
          iteration: 0,
          model: "m",
          status: "done",
          inputTokens: 1000,
          outputTokens: 500,
          durationMs: 60000,
        }),
        buildDevRow({
          agent: "b",
          category: "quality-loop",
          iteration: 1,
          model: "m",
          status: "done",
          inputTokens: 2000,
          outputTokens: 0,
          durationMs: 30000,
          isLoop: true,
        }),
      ];

      expect(devLoopTotals(rows)).toMatchObject({
        tokens: 3500,
        ms: 90000,
        tokensLabel: "3.5k",
        durationLabel: "1m 30s",
      });
    });

    it("derives progress behind its own seam, counting failures as terminal", () => {
      const rows = [
        buildDevRow({ agent: "a", category: "c", model: "m", status: "failed" }),
        buildDevRow({ agent: "b", category: "c", model: "m", status: "running" }),
      ];

      expect(devLoopProgress(rows)).toEqual({ done: 1, total: 2, pct: 50 });
      expect(devLoopProgress([])).toEqual({ done: 0, total: 0, pct: 0 });
    });
  });

  describe("detail rows", () => {
    const step = (overrides = {}) => ({
      agent: "feature-builder",
      iteration: 0,
      model: "m1",
      status: "done",
      inputTokens: 100,
      outputTokens: 50,
      durationMs: 61000,
      ...overrides,
    });

    it("maps the flat steps list with tokens and duration", () => {
      const run = {
        ...baseRun,
        stages: loopStages,
        steps: [step(), step({ agent: "test-runner", iteration: 0, model: "m2" })],
      };

      const view = devLoop(run);

      expect(view.hasDevLoop).toBe(true);
      expect(view.devLoop).toHaveLength(2);
      expect(view.devLoop[0]).toMatchObject({
        agent: "feature-builder",
        iteration: 0,
        model: "m1",
        statusClass: "stage-done",
        isLoop: false,
        showIteration: false,
        inputTokens: 100,
        outputTokens: 50,
        durationMs: 61000,
      });
      expect(view.devLoop[0].stats).toBe("150 · 1m 01s");
    });

    it("reads loop membership from the seeded stage type, not a frontend hardcode", () => {
      const run = {
        ...baseRun,
        stages: loopStages,
        steps: [
          step({ agent: "feature-builder", category: "implementation", iteration: 0 }),
          step({ agent: "static-analysis", category: "quality-loop", iteration: 1, model: "m2" }),
          step({ agent: "feature-builder", category: "quality-loop", iteration: 1, model: "m1" }),
        ],
      };

      const view = devLoop(run);

      expect(view.devLoop[0].isLoop).toBe(false);
      expect(view.devLoop[0].showIteration).toBe(false);
      expect(view.devLoop[1].isLoop).toBe(true);
      expect(view.devLoop[1].showIteration).toBe(false);
      expect(view.devLoop[2].isLoop).toBe(true);
      expect(view.devLoop[2].showIteration).toBe(true);
    });

    it("leaves iteration rows on sequential stages unindented", () => {
      const run = {
        ...baseRun,
        stages: loopStages,
        steps: [step({ agent: "test-runner", category: "test-rerun", iteration: 1 })],
      };

      const view = devLoop(run);

      expect(view.devLoop[0].isLoop).toBe(false);
    });

    it("renders a lone iteration:1 on a sequential type unindented", () => {
      const run = {
        ...baseRun,
        stages: [
          {
            agent: "test-rerun",
            category: "test-rerun",
            model: "m1",
            status: "done",
            type: "sequential",
          },
        ],
        steps: [step({ agent: "test-runner", category: "test-rerun", iteration: 1 })],
      };

      const view = devLoop(run);

      expect(view.devLoop).toHaveLength(1);
      expect(view.devLoop[0].isLoop).toBe(false);
      expect(view.devLoop[0].showIteration).toBe(false);
    });

    it("keeps the phase-4 test re-run as a numbered outer step", () => {
      const run = {
        ...baseRun,
        stages: loopStages,
        steps: [
          step({ agent: "test-runner", category: "implementation", iteration: 0 }),
          step({ agent: "test-runner", category: "test-rerun", iteration: 1 }),
        ],
      };

      const view = devLoop(run);

      expect(view.devLoop[1].isLoop).toBe(false);
      expect(view.devLoop[1].showIteration).toBe(true);
    });

    it("maps failed steps to the failed colour class", () => {
      const run = { ...baseRun, stages: loopStages, steps: [step({ status: "failed" })] };

      expect(devLoop(run).devLoop[0].statusClass).toBe("stage-failed");
    });

    it("falls back to seeded stages when steps are empty", () => {
      const run = {
        ...baseRun,
        stages: [{ agent: "feature-builder", model: "m1", status: "done" }],
        steps: [],
      };

      const view = devLoop(run);

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
          {
            agent: "quality-loop",
            category: "quality-loop",
            model: "m2",
            status: "running",
            type: "loop",
          },
        ],
        steps: [],
      };

      const view = devLoop(run);

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

      const view = devLoop(run);

      expect(view.devLoop).toHaveLength(1);
      expect(view.devLoop[0].agent).toBe("test-runner");
    });

    it("totals tokens and duration across the detail rows", () => {
      const run = {
        ...baseRun,
        stages: loopStages,
        steps: [
          step({ inputTokens: 1000, outputTokens: 500, durationMs: 60000 }),
          step({ agent: "test-runner", inputTokens: 2000, outputTokens: 0, durationMs: 30000 }),
        ],
      };

      const view = devLoop(run);

      expect(view.devLoopTotals.tokens).toBe(3500);
      expect(view.devLoopTotals.ms).toBe(90000);
      expect(view.devLoopTotals.tokensLabel).toBe("3.5k");
      expect(view.devLoopTotals.durationLabel).toBe("1m 30s");
    });

    it("exposes no stages/steps/hasSteps outputs: stages stay input shape", () => {
      const run = {
        ...baseRun,
        stages: loopStages,
        steps: [step()],
      };

      const view = devLoop(run);

      expect(view).not.toHaveProperty("stages");
      expect(view).not.toHaveProperty("steps");
      expect(view).not.toHaveProperty("hasSteps");
    });

    it("reports no detail rows when neither steps nor stages exist", () => {
      const view = devLoop(baseRun);

      expect(view.hasDevLoop).toBe(false);
      expect(view.devLoop).toEqual([]);
    });

    it("reports zero progress when neither steps nor stages exist", () => {
      const view = devLoop(baseRun);

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
      ...overrides,
    });

    it("reports full progress when every step is done", () => {
      const run = {
        ...baseRun,
        stages: loopStages,
        steps: [step(), step({ agent: "test-runner" })],
      };

      expect(devLoop(run).devLoopProgress).toEqual({ done: 2, total: 2, pct: 100 });
    });

    it("reports partial progress for a mix of done and running steps", () => {
      const run = {
        ...baseRun,
        stages: loopStages,
        steps: [step(), step({ agent: "test-runner", status: "running" })],
      };

      expect(devLoop(run).devLoopProgress).toEqual({ done: 1, total: 2, pct: 50 });
    });

    it("counts failed steps as terminal", () => {
      const run = {
        ...baseRun,
        stages: loopStages,
        steps: [step({ status: "failed" }), step({ agent: "test-runner", status: "running" })],
      };

      expect(devLoop(run).devLoopProgress).toEqual({ done: 1, total: 2, pct: 50 });
    });

    it("reports zero progress while every step is still running", () => {
      const run = {
        ...baseRun,
        stages: loopStages,
        steps: [step({ status: "running" }), step({ agent: "test-runner", status: "pending" })],
      };

      expect(devLoop(run).devLoopProgress).toEqual({ done: 0, total: 2, pct: 0 });
    });

    it("derives progress from the stages fallback when steps are empty", () => {
      const run = {
        ...baseRun,
        stages: [{ agent: "feature-builder", model: "m1", status: "done" }],
        steps: [],
      };

      expect(devLoop(run).devLoopProgress).toEqual({ done: 1, total: 1, pct: 100 });
    });
  });

  describe("grouped detail (categories)", () => {
    const categorizedStages = [
      {
        agent: "implementation",
        category: "implementation",
        model: "m1",
        status: "running",
        type: "sequential",
      },
      {
        agent: "quality-loop",
        category: "quality-loop",
        model: "m2",
        status: "pending",
        type: "loop",
      },
      {
        agent: "test-rerun",
        category: "test-rerun",
        model: "m1",
        status: "pending",
        type: "sequential",
      },
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
      ...overrides,
    });

    it("shows category dots only while the run is live: one header per stage, no rows", () => {
      const run = { ...baseRun, status: "running", stages: categorizedStages, steps: [] };

      const view = devLoop(run);

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

      const view = devLoop(run);

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

      const view = devLoop(run);
      const loop = view.devGroups[1];

      expect(loop.steps[0]).toMatchObject({ isLoop: true, showIteration: false });
      expect(loop.steps[1]).toMatchObject({ isLoop: true, showIteration: true });
      expect(loop.steps[1]).toMatchObject({
        inputTokens: 100,
        outputTokens: 50,
        durationMs: 61000,
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

      const view = devLoop(run);

      expect(view.devGroups[0].totals).toMatchObject({
        tokens: 1500,
        ms: 60000,
        tokensLabel: "1.5k",
        durationLabel: "1m 00s",
      });
      expect(view.devGroups[1].totals).toMatchObject({ tokens: 2000, ms: 30000 });
      expect(view.devLoopTotals).toMatchObject({ tokens: 3500, ms: 90000 });
    });

    it("trusts the seeded stage status for finished headers, not step aggregation", () => {
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

      const view = devLoop(failed);

      expect(view.devGroups[0].statusClass).toBe("stage-running");
      expect(view.devGroups[1].statusClass).toBe("stage-pending");
    });

    it("groups steps without a category under the uncategorized bucket", () => {
      const run = {
        ...baseRun,
        status: "done",
        stages: [{ agent: "feature-builder", model: "m1", status: "done" }],
        steps: [{ ...step(), category: undefined }],
      };

      const view = devLoop(run);

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

      const view = devLoop(run);

      expect(view.devGroups.map((g) => g.category)).toEqual(["implementation", "uncategorized"]);
      expect(view.devGroups[0].steps).toHaveLength(1);
      expect(view.devGroups[0].statusClass).toBe("stage-done");
      expect(view.devGroups[1].steps.map((s) => s.agent)).toEqual(["ghost"]);
    });

    it("routes rows whose category matches no stage into the single uncategorized group", () => {
      const run = {
        ...baseRun,
        status: "done",
        stages: [
          { agent: "implementation", category: "implementation", model: "m1", status: "done" },
        ],
        steps: [step(), step({ agent: "ghost", category: "ghost" })],
      };

      const view = devLoop(run);

      expect(view.devGroups.map((g) => g.category)).toEqual(["implementation", "uncategorized"]);
      expect(view.devGroups[1]).toMatchObject({ model: "—", statusClass: "stage-pending" });
      expect(view.devGroups[1].steps.map((s) => s.agent)).toEqual(["ghost"]);
    });

    it("reports no groups when neither steps nor stages exist", () => {
      const view = devLoop(baseRun);

      expect(view.hasDevGroups).toBe(false);
      expect(view.devGroups).toEqual([]);
    });
  });
});
