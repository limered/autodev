import { describe, it, expect } from "vitest";
import { queueRowView } from "../../models/queueView.js";

// Row view-model for one queue item: a single queueRowView call derives the
// badge status text and the running/failed gating the RunQueueColumn template
// needs per row, so the runId/runStatus branching is table-tested once
// through this seam instead of through four helpers. The status-* class lives
// in the template (`status-${status}`), not in this model.
describe("queueRowView", () => {
  it("views an item with no linked run as queued and idle", () => {
    expect(queueRowView({ runId: null })).toEqual({
      status: "queued",
      isRunning: false,
      isFailed: false,
    });
  });

  it("views a reserved run id with no run event yet as starting and running", () => {
    for (const runStatus of [null, undefined]) {
      expect(queueRowView({ runId: "r1", runStatus })).toEqual({
        status: "starting",
        isRunning: true,
        isFailed: false,
      });
    }
  });

  it("views launching, running, stalled, and starting runs as running", () => {
    for (const runStatus of ["launching", "running", "stalled", "starting"]) {
      expect(queueRowView({ runId: "r1", runStatus })).toEqual({
        status: "running",
        isRunning: true,
        isFailed: false,
      });
    }
  });

  it("views finished runs as done and not running", () => {
    expect(queueRowView({ runId: "r1", runStatus: "done" })).toEqual({
      status: "done",
      isRunning: false,
      isFailed: false,
    });
  });

  it("views failed runs as failed and restartable", () => {
    expect(queueRowView({ runId: "r1", runStatus: "failed" })).toEqual({
      status: "failed",
      isRunning: false,
      isFailed: true,
    });
  });
});
