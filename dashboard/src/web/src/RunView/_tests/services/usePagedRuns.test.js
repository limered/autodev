import { describe, it, expect, vi } from "vitest";
import { usePagedRuns } from "../../services/usePagedRuns.js";

function pageResponse(items) {
  return { ok: true, status: 200, json: () => Promise.resolve(items) };
}

// Deterministic run fixtures. id `run-k` is started k seconds after the epoch so the
// started_at DESC order the API promises maps to descending ids.
function makeRuns(n, startId = 0) {
  return Array.from({ length: n }, (_, i) => ({
    runId: `run-${startId + i}`,
    repo: "owner/repo",
    branch: "main",
    status: "done",
    startedAt: new Date((startId + i) * 1000).toISOString(),
  }));
}

describe("usePagedRuns", () => {
  it("first load yields the first page of 10 runs and keeps hasMore true", async () => {
    const fetchFn = vi.fn(() => Promise.resolve(pageResponse(makeRuns(10))));
    const feed = usePagedRuns(fetchFn);

    await feed.loadFirst();

    expect(fetchFn).toHaveBeenCalledWith("/runs?skip=0&take=10");
    expect(feed.runs.value).toHaveLength(10);
    expect(feed.hasMore.value).toBe(true);
    expect(feed.error.value).toBeNull();
  });

  it("load next appends the next window offset by the rows already loaded", async () => {
    let call = 0;
    const pages = [makeRuns(10, 0), makeRuns(10, 10)];
    const fetchFn = vi.fn(() => Promise.resolve(pageResponse(pages[call++])));
    const feed = usePagedRuns(fetchFn);

    await feed.loadFirst();
    await feed.loadNext();

    expect(fetchFn).toHaveBeenNthCalledWith(2, "/runs?skip=10&take=10");
    expect(feed.runs.value).toHaveLength(20);
    expect(feed.runs.value[10].runId).toBe("run-10");
    expect(feed.hasMore.value).toBe(true);
  });

  it("a short page marks the end and stops further loading", async () => {
    let call = 0;
    const pages = [makeRuns(10, 0), makeRuns(4, 10)]; // short second page
    const fetchFn = vi.fn(() => Promise.resolve(pageResponse(pages[call++])));
    const feed = usePagedRuns(fetchFn);

    await feed.loadFirst();
    await feed.loadNext();

    expect(feed.runs.value).toHaveLength(14);
    expect(feed.hasMore.value).toBe(false);

    await feed.loadNext(); // end reached — must no-op
    expect(fetchFn).toHaveBeenCalledTimes(2);
    expect(feed.runs.value).toHaveLength(14);
  });

  it("marks the end immediately when the first page is short", async () => {
    const fetchFn = vi.fn(() => Promise.resolve(pageResponse(makeRuns(3))));
    const feed = usePagedRuns(fetchFn);

    await feed.loadFirst();

    expect(feed.runs.value).toHaveLength(3);
    expect(feed.hasMore.value).toBe(false);
  });

  it("ignores re-entrant load-next calls while a page is in flight", async () => {
    const firstPage = pageResponse(makeRuns(10, 0));
    let resolveNext;
    const fetchFn = vi.fn((url) => {
      if (url === "/runs?skip=0&take=10") return Promise.resolve(firstPage);
      return new Promise((r) => {
        resolveNext = () => r(pageResponse(makeRuns(10, 10)));
      });
    });
    const feed = usePagedRuns(fetchFn);
    await feed.loadFirst();

    const first = feed.loadNext(); // in flight
    await feed.loadNext(); // re-entrant — must no-op
    expect(fetchFn).toHaveBeenCalledTimes(2); // loadFirst + one loadNext only
    resolveNext();
    await first;
  });

  it("records an error on a failed fetch without dropping already-loaded runs", async () => {
    let call = 0;
    const fetchFn = vi.fn(() => {
      call++;
      if (call === 2) return Promise.resolve({ ok: false, status: 500 });
      return Promise.resolve(pageResponse(makeRuns(10)));
    });
    const feed = usePagedRuns(fetchFn);

    await feed.loadFirst();
    await feed.loadNext(); // 500

    expect(feed.error.value).toBe("HTTP 500");
    expect(feed.runs.value).toHaveLength(10); // first page preserved
    expect(feed.hasMore.value).toBe(true); // end not reached; button can retry
  });

  it("refreshFirst re-fetches the first page once and leaves appended pages untouched", async () => {
    let call = 0;
    // Poll 3 is a first-page refresh after the server gained newer runs.
    const pages = [makeRuns(10, 0), makeRuns(10, 10), makeRuns(10, 100)];
    const fetchFn = vi.fn(() => Promise.resolve(pageResponse(pages[call++])));
    const feed = usePagedRuns(fetchFn);

    await feed.loadFirst();
    await feed.loadNext();
    await feed.refreshFirst();

    expect(fetchFn).toHaveBeenNthCalledWith(3, "/runs?skip=0&take=10");
    expect(fetchFn).toHaveBeenCalledTimes(3); // one refresh fetch, nothing else
    expect(feed.runs.value.slice(0, 10)).toEqual(makeRuns(10, 100)); // fresh first page
    expect(feed.runs.value.slice(10)).toEqual(makeRuns(10, 10)); // appended page kept as loaded
    expect(feed.error.value).toBeNull();
  });

  it("refreshFirst with a short page drops stale appended rows and marks the end", async () => {
    let call = 0;
    const pages = [makeRuns(10, 0), makeRuns(10, 10), makeRuns(3, 100)]; // short refresh
    const fetchFn = vi.fn(() => Promise.resolve(pageResponse(pages[call++])));
    const feed = usePagedRuns(fetchFn);

    await feed.loadFirst();
    await feed.loadNext();
    await feed.refreshFirst();

    expect(feed.runs.value).toEqual(makeRuns(3, 100));
    expect(feed.hasMore.value).toBe(false);
  });

  it("loadNext after a refresh continues from the loaded length", async () => {
    let call = 0;
    const pages = [makeRuns(10, 0), makeRuns(10, 10), makeRuns(10, 100), makeRuns(10, 20)];
    const fetchFn = vi.fn(() => Promise.resolve(pageResponse(pages[call++])));
    const feed = usePagedRuns(fetchFn);

    await feed.loadFirst();
    await feed.loadNext();
    await feed.refreshFirst();
    await feed.loadNext();

    expect(fetchFn).toHaveBeenNthCalledWith(4, "/runs?skip=20&take=10");
    expect(feed.runs.value).toHaveLength(30);
  });

  it("a failed refresh records the error and keeps the loaded runs", async () => {
    let call = 0;
    const fetchFn = vi.fn(() => {
      call++;
      if (call === 2) return Promise.resolve({ ok: false, status: 500 });
      return Promise.resolve(pageResponse(makeRuns(10)));
    });
    const feed = usePagedRuns(fetchFn);

    await feed.loadFirst();
    await feed.refreshFirst(); // 500

    expect(feed.error.value).toBe("HTTP 500");
    expect(feed.runs.value).toHaveLength(10); // loaded page preserved
  });

  it("a refresh after the end was reached does not resurrect Load more", async () => {
    let call = 0;
    // Second page is short → end reached with 13 rows loaded; the refresh page
    // is full but appended rows exist, so the end must stand.
    const pages = [makeRuns(10, 0), makeRuns(3, 10), makeRuns(10, 100)];
    const fetchFn = vi.fn(() => Promise.resolve(pageResponse(pages[call++])));
    const feed = usePagedRuns(fetchFn);

    await feed.loadFirst();
    await feed.loadNext();
    await feed.refreshFirst();

    expect(feed.hasMore.value).toBe(false);
    expect(feed.runs.value.slice(0, 10)).toEqual(makeRuns(10, 100)); // fresh first page
    expect(feed.runs.value.slice(10)).toEqual(makeRuns(3, 10)); // appended rows kept
  });

  it("exposes the terminal-only visible window while paging over the raw window", async () => {
    const mixed = [
      ...makeRuns(6, 0).map((r) => ({ ...r, status: "done" })),
      { runId: "run-a", repo: "owner/repo", branch: "main", status: "running" },
      { runId: "run-b", repo: "owner/repo", branch: "main", status: "stalled" },
      { runId: "run-c", repo: "owner/repo", branch: "main", status: "launching" },
      { runId: "run-x", repo: "owner/repo", branch: "main", status: "cancelled" },
    ];
    const fetchFn = vi.fn(() => Promise.resolve(pageResponse(mixed)));
    const feed = usePagedRuns(fetchFn);

    await feed.loadFirst();

    expect(feed.runs.value).toHaveLength(10); // raw window drives skip/hasMore
    expect(feed.historyRuns.value.map((r) => r.runId)).toEqual(makeRuns(6, 0).map((r) => r.runId));
    expect(feed.hasMore.value).toBe(true); // full raw page: end not reached
  });
});
