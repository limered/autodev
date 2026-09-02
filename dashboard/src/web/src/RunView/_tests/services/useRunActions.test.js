import { describe, it, expect, vi } from "vitest";
import { useRunActions } from "../../services/useRunActions.js";

function okFetch() {
  return vi.fn(() => Promise.resolve({ ok: true, status: 204 }));
}

describe("useRunActions", () => {
  it("deleteRun DELETEs the run and reports success", async () => {
    const fetchFn = okFetch();
    const actions = useRunActions(fetchFn);

    const ok = await actions.deleteRun("abc-123");

    expect(ok).toBe(true);
    expect(fetchFn).toHaveBeenCalledWith("/runs/abc-123", { method: "DELETE" });
    expect(actions.deleteError.value).toBeNull();
  });

  it("records an error on a non-ok response", async () => {
    const fetchFn = vi.fn(() => Promise.resolve({ ok: false, status: 404 }));
    const actions = useRunActions(fetchFn);

    const ok = await actions.deleteRun("gone");

    expect(ok).toBe(false);
    expect(actions.deleteError.value).toBe("HTTP 404");
  });

  it("ignores re-entrant calls while in flight", async () => {
    let resolve;
    const fetchFn = vi.fn(
      () =>
        new Promise((r) => {
          resolve = () => r({ ok: true, status: 204 });
        }),
    );
    const actions = useRunActions(fetchFn);

    const first = actions.deleteRun("x");
    const second = await actions.deleteRun("x"); // in-flight, should no-op

    expect(second).toBe(false);
    expect(fetchFn).toHaveBeenCalledTimes(1);
    resolve();
    await first;
  });
});
