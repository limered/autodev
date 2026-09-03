import { describe, it, expect } from "vitest";
import {
  queueStatus,
  queueStatusClass,
  isQueueItemRunning,
  isQueueItemFailed,
  resolveLocalQueue,
} from "../../models/queueView.js";

describe("queueStatus", () => {
  it("returns queued when no run is linked", () => {
    expect(queueStatus({ runId: null })).toBe("queued");
  });

  it("returns starting when a run id is reserved but no run event has arrived", () => {
    expect(queueStatus({ runId: "r1", runStatus: null })).toBe("starting");
    expect(queueStatus({ runId: "r1", runStatus: undefined })).toBe("starting");
  });

  it("returns running for launching, running, or stalled runs", () => {
    expect(queueStatus({ runId: "r1", runStatus: "launching" })).toBe("running");
    expect(queueStatus({ runId: "r1", runStatus: "running" })).toBe("running");
    expect(queueStatus({ runId: "r1", runStatus: "stalled" })).toBe("running");
  });

  it("returns done for finished runs", () => {
    expect(queueStatus({ runId: "r1", runStatus: "done" })).toBe("done");
  });

  it("returns failed for failed runs", () => {
    expect(queueStatus({ runId: "r1", runStatus: "failed" })).toBe("failed");
  });
});

describe("queueStatusClass", () => {
  it("prefixes the inferred status with status-", () => {
    expect(queueStatusClass({ runId: null })).toBe("status-queued");
    expect(queueStatusClass({ runId: "r1", runStatus: "done" })).toBe("status-done");
    expect(queueStatusClass({ runId: "r1", runStatus: null })).toBe("status-starting");
  });
});

describe("isQueueItemRunning", () => {
  it("returns false when no run is linked", () => {
    expect(isQueueItemRunning({ runId: null })).toBe(false);
  });

  it("returns true for active run statuses", () => {
    expect(isQueueItemRunning({ runId: "r1", runStatus: "launching" })).toBe(true);
    expect(isQueueItemRunning({ runId: "r1", runStatus: "running" })).toBe(true);
    expect(isQueueItemRunning({ runId: "r1", runStatus: "stalled" })).toBe(true);
    expect(isQueueItemRunning({ runId: "r1", runStatus: "starting" })).toBe(true);
  });

  it("returns false for terminal run statuses", () => {
    expect(isQueueItemRunning({ runId: "r1", runStatus: "done" })).toBe(false);
    expect(isQueueItemRunning({ runId: "r1", runStatus: "failed" })).toBe(false);
  });
});

describe("isQueueItemFailed", () => {
  it("returns false when no run is linked", () => {
    expect(isQueueItemFailed({ runId: null })).toBe(false);
  });

  it("returns false for non-failed statuses", () => {
    expect(isQueueItemFailed({ runId: "r1", runStatus: "running" })).toBe(false);
    expect(isQueueItemFailed({ runId: "r1", runStatus: "done" })).toBe(false);
  });

  it("returns true for failed runs", () => {
    expect(isQueueItemFailed({ runId: "r1", runStatus: "failed" })).toBe(true);
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
