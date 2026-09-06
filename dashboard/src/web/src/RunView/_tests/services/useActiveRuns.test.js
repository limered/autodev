import { describe, it, expect, vi } from "vitest";
import { useActiveRuns } from "../../services/useActiveRuns.js";

function ok(items) {
  return { ok: true, status: 200, json: () => Promise.resolve(items) };
}

const run = (id, status) => ({
  runId: id,
  repo: "owner/repo",
  branch: "main",
  status,
  startedAt: "2026-08-30T00:00:00Z",
});

describe("useActiveRuns", () => {
  it("fetches /runs/active and lists only non-terminal runs", async () => {
    const fetchFn = vi.fn(() =>
      Promise.resolve(
        ok([
          run("a", "launching"),
          run("b", "running"),
          run("c", "stalled"),
          run("d", "done"),
          run("e", "failed"),
        ]),
      ),
    );
    const feed = useActiveRuns(fetchFn);

    await feed.load();

    expect(fetchFn).toHaveBeenCalledWith("/runs/active");
    expect(feed.runs.value.map((r) => r.runId)).toEqual(["a", "b", "c"]);
    expect(feed.error.value).toBeNull();
  });

  it("seeds the active set on the first load without reporting a change", async () => {
    const onSetChange = vi.fn();
    const fetchFn = vi.fn(() => Promise.resolve(ok([run("a", "running")])));
    const feed = useActiveRuns(fetchFn, { onSetChange });

    await feed.load();

    expect(onSetChange).not.toHaveBeenCalled();
  });

  it("raises no change while repeated polls return the same active set", async () => {
    const onSetChange = vi.fn();
    const fetchFn = vi.fn(() => Promise.resolve(ok([run("a", "running")])));
    const feed = useActiveRuns(fetchFn, { onSetChange });

    await feed.load(); // seed
    await feed.load(); // same set
    await feed.load(); // same set

    expect(onSetChange).not.toHaveBeenCalled();
  });

  it("raises no change for status moves inside the active set", async () => {
    const onSetChange = vi.fn();
    let status = "launching";
    const fetchFn = vi.fn(() => Promise.resolve(ok([run("a", status)])));
    const feed = useActiveRuns(fetchFn, { onSetChange });

    await feed.load(); // seed: launching
    status = "running";
    await feed.load(); // same id, new status — history unaffected
    status = "stalled";
    await feed.load();

    expect(onSetChange).not.toHaveBeenCalled();
  });

  it("reports one change when a run starts (new id enters the set)", async () => {
    const onSetChange = vi.fn();
    let items = [];
    const fetchFn = vi.fn(() => Promise.resolve(ok(items)));
    const feed = useActiveRuns(fetchFn, { onSetChange });

    await feed.load(); // seed: idle
    items = [run("a", "running")];
    await feed.load(); // a started

    expect(onSetChange).toHaveBeenCalledTimes(1);
  });

  it("reports one change when a run finishes (id leaves the set)", async () => {
    const onSetChange = vi.fn();
    let items = [run("a", "running")];
    const fetchFn = vi.fn(() => Promise.resolve(ok(items)));
    const feed = useActiveRuns(fetchFn, { onSetChange });

    await feed.load(); // seed: a active
    items = [];
    await feed.load(); // a finished

    expect(onSetChange).toHaveBeenCalledTimes(1);
  });

  it("reports one change when a poll both finishes and starts a run", async () => {
    const onSetChange = vi.fn();
    let items = [run("a", "running")];
    const fetchFn = vi.fn(() => Promise.resolve(ok(items)));
    const feed = useActiveRuns(fetchFn, { onSetChange });

    await feed.load();
    items = [run("b", "launching")];
    await feed.load();

    expect(onSetChange).toHaveBeenCalledTimes(1);
  });

  it("fires no change when a delete-then-reload finds an already-dropped run", async () => {
    // The delete handler just re-polls through this diff (no manual emit), so
    // deleting a run that already left the set must not refresh history.
    const onSetChange = vi.fn();
    let items = [run("a", "running")];
    const fetchFn = vi.fn(() => Promise.resolve(ok(items)));
    const feed = useActiveRuns(fetchFn, { onSetChange });

    await feed.load(); // seed: a active
    items = [];
    await feed.load(); // a finished on its own — one change
    expect(onSetChange).toHaveBeenCalledTimes(1);
    await feed.load(); // user deletes the already-dropped run, set unchanged
    expect(onSetChange).toHaveBeenCalledTimes(1);
  });

  it("a failed poll raises no change, surfaces the error and keeps the known set", async () => {
    const onSetChange = vi.fn();
    let items = [run("a", "running")];
    let healthy = true;
    const fetchFn = vi.fn(() =>
      healthy ? Promise.resolve(ok(items)) : Promise.resolve({ ok: false, status: 500 }),
    );
    const feed = useActiveRuns(fetchFn, { onSetChange });

    await feed.load(); // seed
    healthy = false;
    await feed.load(); // network failure must not look like a finish

    expect(onSetChange).not.toHaveBeenCalled();
    expect(feed.error.value).toBe("HTTP 500");
    expect(feed.runs.value.map((r) => r.runId)).toEqual(["a"]); // last good poll kept

    healthy = true;
    await feed.load(); // same set returns — still no change
    expect(onSetChange).not.toHaveBeenCalled();
    expect(feed.error.value).toBeNull();
  });
});
