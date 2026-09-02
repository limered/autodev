import { describe, it, expect, vi } from "vitest";
import { useQueueActions } from "../../services/useQueueActions.js";

function okFetch() {
  return vi.fn(() => Promise.resolve({ ok: true, status: 200 }));
}

describe("useQueueActions", () => {
  it("enqueue POSTs the issueId and reports success", async () => {
    const fetchFn = okFetch();
    const actions = useQueueActions(fetchFn);

    const ok = await actions.enqueue(42);

    expect(ok).toBe(true);
    expect(fetchFn).toHaveBeenCalledWith(
      "/queue",
      expect.objectContaining({
        method: "POST",
        body: JSON.stringify({ issueId: 42 }),
      }),
    );
    expect(actions.enqueueError.value).toBeNull();
  });

  it("reorder PATCHes the id list", async () => {
    const fetchFn = okFetch();
    const actions = useQueueActions(fetchFn);

    await actions.reorder([3, 1, 2]);

    expect(fetchFn).toHaveBeenCalledWith(
      "/queue/order",
      expect.objectContaining({
        method: "PATCH",
        body: JSON.stringify({ ids: [3, 1, 2] }),
      }),
    );
  });

  it("remove/startNext/restart hit the right routes", async () => {
    const fetchFn = okFetch();
    const actions = useQueueActions(fetchFn);

    await actions.remove(7);
    await actions.startNext(7);
    await actions.restart(7);

    expect(fetchFn).toHaveBeenCalledWith("/queue/7", { method: "DELETE" });
    expect(fetchFn).toHaveBeenCalledWith("/queue/7/start-next", { method: "POST" });
    expect(fetchFn).toHaveBeenCalledWith("/queue/7/restart", { method: "POST" });
  });

  it("records a per-action error on a non-ok response", async () => {
    const fetchFn = vi.fn(() => Promise.resolve({ ok: false, status: 500 }));
    const actions = useQueueActions(fetchFn);

    const ok = await actions.remove(1);

    expect(ok).toBe(false);
    expect(actions.removeError.value).toBe("HTTP 500");
    expect(actions.enqueueError.value).toBeNull(); // errors are isolated per action
  });

  it("ignores re-entrant calls while an action is in flight", async () => {
    let resolve;
    const fetchFn = vi.fn(
      () =>
        new Promise((r) => {
          resolve = () => r({ ok: true, status: 200 });
        }),
    );
    const actions = useQueueActions(fetchFn);

    const first = actions.enqueue(1);
    const second = await actions.enqueue(1); // in-flight, should no-op

    expect(second).toBe(false);
    expect(fetchFn).toHaveBeenCalledTimes(1);
    resolve();
    await first;
  });
});
