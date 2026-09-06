import { describe, it, expect, vi } from "vitest";
import { useQueueHandlers } from "../../services/useQueueHandlers.js";

// Mirrored seam test for the shared run()-then-load() wiring (Block A): each
// queue write is followed by the feed reloads that show its effect. The two
// columns hand in their feed loads and bind the returned handlers, so this
// file pins the reload policy exactly as the columns rely on it — which feeds
// refresh on success, which never refresh on failure, and the one
// unconditional re-sync (reorder) that must reconverge even on persist
// failure — plus error surfacing through the returned refs.
function okFetch(status = 200) {
  return vi.fn(() => Promise.resolve({ ok: true, status }));
}

function failFetch(status = 500) {
  return vi.fn(() => Promise.resolve({ ok: false, status }));
}

function makeReloads() {
  return {
    reloadIssues: vi.fn(() => Promise.resolve()),
    reloadQueue: vi.fn(() => Promise.resolve()),
  };
}

function makeHandlers(fetchFn, reloads) {
  return useQueueHandlers({ fetchFn, ...reloads });
}

describe("useQueueHandlers reload policy", () => {
  it("reloads both feeds after a successful enqueue", async () => {
    const reloads = makeReloads();
    const { enqueue } = makeHandlers(okFetch(201), reloads);

    await enqueue({ gitHubId: 101 });

    expect(reloads.reloadIssues).toHaveBeenCalledTimes(1);
    expect(reloads.reloadQueue).toHaveBeenCalledTimes(1);
  });

  it("reloads nothing and surfaces the error when an enqueue fails", async () => {
    const reloads = makeReloads();
    const { enqueue, enqueueError } = makeHandlers(failFetch(503), reloads);

    await enqueue({ gitHubId: 101 });

    expect(reloads.reloadIssues).not.toHaveBeenCalled();
    expect(reloads.reloadQueue).not.toHaveBeenCalled();
    expect(enqueueError.value).toBe("HTTP 503");
  });

  it("reloads both feeds after a successful remove", async () => {
    const reloads = makeReloads();
    const { remove } = makeHandlers(okFetch(), reloads);

    await remove({ id: "q1" });

    expect(reloads.reloadIssues).toHaveBeenCalledTimes(1);
    expect(reloads.reloadQueue).toHaveBeenCalledTimes(1);
  });

  it("reloads nothing and surfaces the error when a remove fails", async () => {
    const reloads = makeReloads();
    const { remove, removeError } = makeHandlers(failFetch(), reloads);

    await remove({ id: "q1" });

    expect(reloads.reloadIssues).not.toHaveBeenCalled();
    expect(reloads.reloadQueue).not.toHaveBeenCalled();
    expect(removeError.value).toBe("HTTP 500");
  });

  it("reloads both feeds concurrently after a successful enqueue", async () => {
    let releaseIssues;
    const gate = new Promise((resolve) => {
      releaseIssues = resolve;
    });
    const reloadIssues = vi.fn(() => gate);
    const reloadQueue = vi.fn(() => Promise.resolve());
    const { enqueue } = makeHandlers(okFetch(201), { reloadIssues, reloadQueue });

    const pending = enqueue({ gitHubId: 101 });
    // Promise.all starts both loads together: wait until the queue reload has
    // fired, then assert the issues reload fired alongside it even while its
    // promise is still pending.
    await vi.waitFor(() => expect(reloadQueue).toHaveBeenCalledTimes(1));
    expect(reloadIssues).toHaveBeenCalledTimes(1);
    expect(reloadQueue).toHaveBeenCalledTimes(1);
    releaseIssues();
    await pending;
  });

  it("reloads only the queue after a successful start-next", async () => {
    const reloads = makeReloads();
    const { startNext } = makeHandlers(okFetch(), reloads);

    await startNext({ id: "q1" });

    expect(reloads.reloadQueue).toHaveBeenCalledTimes(1);
    expect(reloads.reloadIssues).not.toHaveBeenCalled();
  });

  it("reloads nothing and surfaces the error when start-next fails", async () => {
    const reloads = makeReloads();
    const { startNext, startNextError } = makeHandlers(failFetch(), reloads);

    await startNext({ id: "q1" });

    expect(reloads.reloadQueue).not.toHaveBeenCalled();
    expect(reloads.reloadIssues).not.toHaveBeenCalled();
    expect(startNextError.value).toBe("HTTP 500");
  });

  it("reloads only the queue after a successful restart", async () => {
    const reloads = makeReloads();
    const { restart } = makeHandlers(okFetch(), reloads);

    await restart({ id: "q2" });

    expect(reloads.reloadQueue).toHaveBeenCalledTimes(1);
    expect(reloads.reloadIssues).not.toHaveBeenCalled();
  });

  it("reloads nothing and surfaces the error when a restart fails", async () => {
    const reloads = makeReloads();
    const { restart, restartError } = makeHandlers(failFetch(), reloads);

    await restart({ id: "q2" });

    expect(reloads.reloadQueue).not.toHaveBeenCalled();
    expect(reloads.reloadIssues).not.toHaveBeenCalled();
    expect(restartError.value).toBe("HTTP 500");
  });

  it("reloads the queue (and not the issues) after a successful reorder", async () => {
    const reloads = makeReloads();
    const { reorder } = makeHandlers(okFetch(), reloads);

    await reorder(["q2", "q1"]);

    expect(reloads.reloadQueue).toHaveBeenCalledTimes(1);
    expect(reloads.reloadIssues).not.toHaveBeenCalled();
  });

  it("still reloads the queue and surfaces the error when a reorder persist fails", async () => {
    const reloads = makeReloads();
    const { reorder, reorderError } = makeHandlers(failFetch(), reloads);

    await reorder(["q2", "q1"]);

    // Unconditional re-sync: the drop already moved optimistically, so the
    // queue must reconverge on server truth even when the persist failed.
    expect(reloads.reloadQueue).toHaveBeenCalledTimes(1);
    expect(reloads.reloadIssues).not.toHaveBeenCalled();
    expect(reorderError.value).toBe("HTTP 500");
  });

  it("exposes isSaving while any queue write is in flight", async () => {
    let release;
    const gate = new Promise((r) => (release = r));
    const reloads = makeReloads();
    const handlers = makeHandlers(
      vi.fn(() => gate),
      reloads,
    );

    expect(handlers.isSaving.value).toBe(false);

    const pending = handlers.startNext({ id: "q1" });
    await new Promise((r) => setTimeout(r, 0));
    expect(handlers.isStartingNext.value).toBe(true);
    expect(handlers.isSaving.value).toBe(true);

    release({ ok: true, status: 200 });
    await pending;
    expect(handlers.isSaving.value).toBe(false);
  });
});
