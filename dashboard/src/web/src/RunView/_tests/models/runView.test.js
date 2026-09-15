import { describe, it, expect } from "vitest";
import { runView } from "../../models/runView.js";
import { devLoopView } from "../../models/devLoopView.js";

describe("runView", () => {
  const nowMs = new Date("2024-06-15T12:00:00Z").getTime();

  const baseRun = {
    runId: "run-1",
    repo: "owner/repo",
    branch: "main",
    model: "test-model",
    status: "running",
    startedAt: new Date(nowMs - 5 * 60 * 1000).toISOString(),
  };

  it("returns fresh values for a recently-seen running run", () => {
    const run = {
      ...baseRun,
      lastHeartbeatAt: new Date(nowMs - 10 * 1000).toISOString(),
    };

    const view = runView(run, nowMs);

    expect(view.lastSeen).toBe("10s ago");
    expect(view.freshnessClass).toBe("fresh");
    expect(view.statusClass).toBe("status-running");
    expect(view.started).not.toBe("—");
  });

  it("warns when the heartbeat is moderately stale", () => {
    const run = {
      ...baseRun,
      lastHeartbeatAt: new Date(nowMs - 45 * 1000).toISOString(),
    };

    expect(runView(run, nowMs).freshnessClass).toBe("stale-warn");
    expect(runView(run, nowMs).lastSeen).toBe("45s ago");
  });

  it("marks very stale running runs as danger", () => {
    const run = {
      ...baseRun,
      lastHeartbeatAt: new Date(nowMs - 125 * 1000).toISOString(),
    };

    expect(runView(run, nowMs).freshnessClass).toBe("stale-danger");
    expect(runView(run, nowMs).lastSeen).toBe("2m 5s ago");
  });

  it("treats done and failed runs as settled regardless of heartbeat age", () => {
    const doneRun = {
      ...baseRun,
      status: "done",
      lastHeartbeatAt: new Date(nowMs - 300 * 1000).toISOString(),
    };
    const failedRun = {
      ...baseRun,
      status: "failed",
      lastHeartbeatAt: new Date(nowMs - 300 * 1000).toISOString(),
    };

    expect(runView(doneRun, nowMs).freshnessClass).toBe("settled");
    expect(runView(doneRun, nowMs).statusClass).toBe("status-done");
    expect(runView(failedRun, nowMs).freshnessClass).toBe("settled");
    expect(runView(failedRun, nowMs).statusClass).toBe("status-failed");
  });

  it("honours a stalled status even when the heartbeat is fresh", () => {
    const run = {
      ...baseRun,
      status: "stalled",
      lastHeartbeatAt: new Date(nowMs - 5 * 1000).toISOString(),
    };

    expect(runView(run, nowMs).freshnessClass).toBe("stale-warn");
    expect(runView(run, nowMs).lastSeen).toBe("5s ago");
  });

  it("does not escalate stalled runs to danger based on heartbeat age", () => {
    const run = {
      ...baseRun,
      status: "stalled",
      lastHeartbeatAt: new Date(nowMs - 300 * 1000).toISOString(),
    };

    expect(runView(run, nowMs).freshnessClass).toBe("stale-warn");
  });

  it("handles missing heartbeat gracefully", () => {
    const run = { ...baseRun, lastHeartbeatAt: null };

    const view = runView(run, nowMs);

    expect(view.lastSeen).toBe("—");
    expect(view.freshnessClass).toBe("unknown");
  });

  it("formats long durations in hours and minutes", () => {
    const run = {
      ...baseRun,
      lastHeartbeatAt: new Date(nowMs - 3665 * 1000).toISOString(),
    };

    expect(runView(run, nowMs).lastSeen).toBe("1h 1m ago");
  });

  it("returns em-dashes for missing started timestamp", () => {
    const run = { ...baseRun, startedAt: null };

    expect(runView(run, nowMs).started).toBe("—");
  });

  describe("time display: terminal runs show a fixed Completed timestamp", () => {
    // The heartbeat sits minutes before the finish so a wrong derivation
    // (lastHeartbeatAt instead of finishedAt) would produce a different string.
    const finishedAt = new Date(nowMs - 30 * 1000).toISOString();
    const heartbeatAt = new Date(nowMs - 300 * 1000).toISOString();

    it("shows a done run as Completed with an absolute timestamp that does not tick", () => {
      const run = {
        ...baseRun,
        status: "done",
        finishedAt,
        lastHeartbeatAt: heartbeatAt,
      };

      const first = runView(run, nowMs);
      const later = runView(run, nowMs + 60 * 1000); // a minute of clock ticks

      expect(first.completed).toBe(new Date(finishedAt).toLocaleString());
      expect(later.completed).toBe(first.completed);
      expect(first.lastSeen).toBeNull();
      expect(first.timeLabel).toBe("Completed");
    });

    it("shows a failed run as Completed with an absolute timestamp that does not tick", () => {
      const run = {
        ...baseRun,
        status: "failed",
        finishedAt,
        lastHeartbeatAt: heartbeatAt,
      };

      const first = runView(run, nowMs);
      const later = runView(run, nowMs + 60 * 1000);

      expect(first.completed).toBe(new Date(finishedAt).toLocaleString());
      expect(later.completed).toBe(first.completed);
      expect(first.lastSeen).toBeNull();
      expect(first.timeLabel).toBe("Completed");
    });

    it("falls back to the last heartbeat when a terminal run has no finishedAt", () => {
      const run = {
        ...baseRun,
        status: "done",
        finishedAt: null,
        lastHeartbeatAt: heartbeatAt,
      };

      const view = runView(run, nowMs);

      expect(view.completed).toBe(new Date(heartbeatAt).toLocaleString());
      expect(view.timeLabel).toBe("Completed");
    });

    it("keeps the live relative Last seen for active runs, ticking with nowMs", () => {
      const run = {
        ...baseRun,
        status: "running",
        lastHeartbeatAt: new Date(nowMs - 10 * 1000).toISOString(),
      };

      const first = runView(run, nowMs);
      const later = runView(run, nowMs + 5 * 1000);

      expect(first.lastSeen).toBe("10s ago");
      expect(later.lastSeen).toBe("15s ago");
      expect(first.completed).toBeNull();
      expect(first.timeLabel).toBe("Last seen");
    });

    it("keeps freshness settled for terminal runs", () => {
      const doneRun = { ...baseRun, status: "done", finishedAt, lastHeartbeatAt: heartbeatAt };
      const failedRun = { ...baseRun, status: "failed", finishedAt, lastHeartbeatAt: heartbeatAt };

      expect(runView(doneRun, nowMs).freshnessClass).toBe("settled");
      expect(runView(failedRun, nowMs).freshnessClass).toBe("settled");
    });
  });

  describe("dev-loop composition: RunCard reads the devLoopView verdict", () => {
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

    it("exposes the dev-loop rows, fallback, groups, totals and progress unchanged", () => {
      const run = {
        ...baseRun,
        stages: [
          { agent: "implementation", category: "implementation", model: "m1", status: "done" },
          { agent: "quality-loop", category: "quality-loop", model: "m2", status: "running" },
        ],
        steps: [
          step(),
          step({ agent: "static-analysis", category: "quality-loop", iteration: 1, model: "m2" }),
        ],
      };

      expect(runView(run, nowMs)).toMatchObject(devLoopView(run));
    });

    it("exposes empty dev-loop verdicts when neither steps nor stages exist", () => {
      expect(runView(baseRun, nowMs)).toMatchObject(devLoopView(baseRun));
    });
  });

  describe("run-to-card seam: pass-through display fields and show* gates", () => {
    const v = (patch) => runView({ ...baseRun, ...patch }, nowMs);

    it("passes status, repo, branch, vm, pr and failure fields through to the card", () => {
      const run = {
        ...baseRun,
        status: "failed",
        repo: "owner/repo",
        branch: "feature/x",
        vmName: "vm-42",
        prUrl: "https://github.com/owner/repo/pull/1",
        failureReason: "agent crashed",
        freezeLocalPath: "/snapshots/run-1",
        freezeCaptured: true,
      };

      const view = runView(run, nowMs);

      expect(view.statusText).toBe("failed");
      expect(view.repo).toBe("owner/repo");
      expect(view.branch).toBe("feature/x");
      expect(view.vmName).toBe("vm-42");
      expect(view.prUrl).toBe("https://github.com/owner/repo/pull/1");
      expect(view.failureReason).toBe("agent crashed");
      expect(view.freezePath).toBe("/snapshots/run-1");
    });

    it("falls back to an em-dash when the run has no freeze path", () => {
      const view = runView({ ...baseRun, freezeLocalPath: null }, nowMs);

      expect(view.freezePath).toBe("—");
    });

    it("exposes no runId on the view: the delete emit keeps the only raw run read", () => {
      const view = runView(baseRun, nowMs);

      expect(view.runId).toBeUndefined();
    });

    it("gates the VM stat on a truthy vmName", () => {
      expect(v({ vmName: "vm-42" }).showVm).toBe(true);
      expect(v({ vmName: "" }).showVm).toBe(false);
      expect(v({ vmName: null }).showVm).toBe(false);
      expect(v({ vmName: undefined }).showVm).toBe(false);
    });

    it("gates the PR row on a truthy prUrl", () => {
      expect(v({ prUrl: "https://github.com/owner/repo/pull/1" }).showPr).toBe(true);
      expect(v({ prUrl: "" }).showPr).toBe(false);
      expect(v({ prUrl: null }).showPr).toBe(false);
      expect(v({ prUrl: undefined }).showPr).toBe(false);
    });

    it("shows the failure row only for failed runs with a reason", () => {
      expect(v({ status: "failed", failureReason: "agent crashed" }).showFailure).toBe(true);
      expect(v({ status: "failed", failureReason: "" }).showFailure).toBe(false);
      expect(v({ status: "failed", failureReason: null }).showFailure).toBe(false);
      expect(v({ status: "failed", failureReason: undefined }).showFailure).toBe(false);
      expect(v({ status: "done", failureReason: "agent crashed" }).showFailure).toBe(false);
      expect(v({ status: "running", failureReason: "agent crashed" }).showFailure).toBe(false);
    });

    it("follows freezeCaptured for the freeze row", () => {
      expect(v({ freezeCaptured: true }).showFreeze).toBe(true);
      expect(v({ freezeCaptured: false }).showFreeze).toBe(false);
      expect(v({ freezeCaptured: undefined }).showFreeze).toBe(false);
      expect(v({ freezeCaptured: null }).showFreeze).toBe(false);
    });
  });
});
