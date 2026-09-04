import { describe, it, expect } from "vitest";
import { queueRowView, resolveLocalQueue } from "../../models/queueView.js";

// Row view-model for one queue item: a single queueRowView call derives the
// badge status text, its status-* class, and the running/failed gating the
// RunQueueColumn template needs per row, so the runId/runStatus branching is
// table-tested once through this seam instead of through four helpers.
describe("queueRowView", () => {
  it("views an item with no linked run as queued and idle", () => {
    expect(queueRowView({ runId: null })).toEqual({
      status: "queued",
      statusClass: "status-queued",
      isRunning: false,
      isFailed: false,
    });
  });

  it("views a reserved run id with no run event yet as starting and running", () => {
    for (const runStatus of [null, undefined]) {
      expect(queueRowView({ runId: "r1", runStatus })).toEqual({
        status: "starting",
        statusClass: "status-starting",
        isRunning: true,
        isFailed: false,
      });
    }
  });

  it("views launching, running, stalled, and starting runs as running", () => {
    for (const runStatus of ["launching", "running", "stalled", "starting"]) {
      expect(queueRowView({ runId: "r1", runStatus })).toEqual({
        status: "running",
        statusClass: "status-running",
        isRunning: true,
        isFailed: false,
      });
    }
  });

  it("views finished runs as done and not running", () => {
    expect(queueRowView({ runId: "r1", runStatus: "done" })).toEqual({
      status: "done",
      statusClass: "status-done",
      isRunning: false,
      isFailed: false,
    });
  });

  it("views failed runs as failed and restartable", () => {
    expect(queueRowView({ runId: "r1", runStatus: "failed" })).toEqual({
      status: "failed",
      statusClass: "status-failed",
      isRunning: false,
      isFailed: true,
    });
  });
});

// Table-tested with no DOM and no fake fetch: this is the pure half of the
// localQueue convergence policy; the ref/watch plumbing in RunQueueColumn
// calls it (issue #75).
describe("resolveLocalQueue", () => {
  const feed = [{ id: "q1" }, { id: "q2" }];
  const local = [{ id: "q2" }, { id: "q1" }];

  it("takes the feed while idle", () => {
    const resolved = resolveLocalQueue(feed, local, { isSaving: false, isDragging: false });

    expect(resolved).toEqual(feed);
    expect(resolved).not.toBe(feed); // copied: the shadow never aliases the feed array
  });

  it("keeps the local order when the feed changes mid-drag", () => {
    expect(resolveLocalQueue(feed, local, { isSaving: false, isDragging: true })).toBe(local);
  });

  it("keeps the local order while a save is in flight", () => {
    expect(resolveLocalQueue(feed, local, { isSaving: true, isDragging: false })).toBe(local);
  });
});
