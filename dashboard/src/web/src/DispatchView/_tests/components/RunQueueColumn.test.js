import { describe, it, expect, vi } from "vitest";
import { createSSRApp, h, nextTick, ref } from "vue";
import { renderToString } from "vue/server-renderer";
import RunQueueColumn from "../../components/RunQueueColumn.vue";
import { useQueueFeedShadow } from "../../services/useQueueFeedShadow.js";
import { useQueueHandlers } from "../../services/useQueueHandlers.js";

// Rendered via SSR (mirroring ErrorBanner's test) so the column's markup
// contract can be asserted without a DOM: row order, status dots, the
// restart/remove controls, Start-next gating, banners, and the empty state.
// The drag-reorder math is covered at the useDragReorder seam; the write
// actions and their busy/error state live inside the column now (issue #75),
// so their markup (busy labels, action-error banners) is beyond this
// harness — the write-then-resync contract is covered at the composable seam
// in the describe below.
async function renderColumn(props) {
  const app = createSSRApp({
    render: () => h(RunQueueColumn, { reloadIssues: vi.fn(), reloadQueue: vi.fn(), ...props }),
  });
  return renderToString(app);
}

const queued = (id, rank, title, overrides = {}) => ({
  id,
  rank,
  issueId: `issue-${id}`,
  issuePresent: true,
  htmlUrl: `https://example.com/issues/${rank}`,
  title,
  repo: "owner/repo",
  number: 100 + rank,
  runId: null,
  runStatus: null,
  ...overrides,
});

const startNextButton = (html) => html.match(/<button[^>]*start-next-button[^>]*>/)?.[0] ?? "";

describe("RunQueueColumn", () => {
  it("renders the queue in order with rank, issue, and a status dot only for non-queued rows", async () => {
    const html = await renderColumn({
      queue: [
        queued("q1", 1, "First task"),
        queued("q2", 2, "Second task", { runId: "r2", runStatus: "running" }),
      ],
    });

    expect(html.indexOf("First task")).toBeLessThan(html.indexOf("Second task"));
    expect(html).toContain("#1");
    expect(html).toContain("#2");
    expect(html).not.toContain("status-queued");
    expect(html).not.toContain('status-dot" title="queued"');
    expect(html).toContain("status-running");
  });

  it("makes rows draggable drop targets for drag-to-reorder", async () => {
    const html = await renderColumn({ queue: [queued("q1", 1, "First task")] });

    expect(html).toContain('class="queue-row"');
    expect(html).toContain('draggable="true"');
  });

  it("marks a queue item whose issue vanished as no longer eligible", async () => {
    const html = await renderColumn({
      queue: [queued("q1", 1, "First task", { issuePresent: false })],
    });

    expect(html).toContain("no longer eligible");
    expect(html).not.toContain("First task");
  });

  it("offers Restart only on failed items", async () => {
    const html = await renderColumn({
      queue: [
        queued("q1", 1, "First task"),
        queued("q2", 2, "Second task", { runId: "r2", runStatus: "failed" }),
      ],
    });

    expect(html.match(/class="restart-button"/g)).toHaveLength(1);
  });

  it("enables Start next only when a queued item is at the head and nothing is running", async () => {
    const idle = await renderColumn({ queue: [queued("q1", 1, "First task")] });
    const running = await renderColumn({
      queue: [
        queued("q1", 1, "First task"),
        queued("q2", 2, "Second task", { runId: "r2", runStatus: "running" }),
      ],
    });
    const empty = await renderColumn({ queue: [] });

    expect(startNextButton(idle)).not.toContain("disabled");
    expect(startNextButton(running)).toContain("disabled");
    expect(startNextButton(empty)).toContain("disabled");
  });

  it("shows the sync error banner", async () => {
    const html = await renderColumn({ queue: [], syncError: "HTTP 500" });

    expect(html).toContain("Failed to load run queue: HTTP 500");
  });

  it("shows the empty state when the queue is empty", async () => {
    const html = await renderColumn({ queue: [] });

    expect(html).toContain("The queue is empty.");
  });

  it("shows the workflow trigger with default name and count on unclaimed rows", async () => {
    const html = await renderColumn({
      queue: [queued("q1", 1, "First task")],
      catalog: {
        defaultWorkflow: "full",
        workflows: [
          { name: "full", stageCount: 5 },
          { name: "quick", stageCount: 2 },
        ],
      },
    });

    expect(html).toContain("workflow-trigger");
    expect(html).toContain("full");
    expect(html).toContain("5");
  });

  it("shows frozen workflow text with no trigger on claimed rows", async () => {
    const html = await renderColumn({
      queue: [queued("q1", 1, "First task", { runId: "r1", runStatus: "running" })],
      catalog: {
        defaultWorkflow: "full",
        workflows: [{ name: "full", stageCount: 5 }],
      },
    });

    expect(html).toContain("frozen");
    expect(html).not.toContain("workflow-trigger");
  });

  it("shows the muted default notice when the factory catalog is missing", async () => {
    const html = await renderColumn({ queue: [queued("q1", 1, "First task")], catalog: null });

    expect(html).toContain("default (no catalog)");
    expect(html).not.toContain("workflow-trigger");
  });
});

function makeReloads() {
  return { reloadIssues: vi.fn(), reloadQueue: vi.fn() };
}

// Shadow convergence through the column's modules: the shadow holds its rows
// while any queue save is in flight, so a poll landing mid-save cannot
// overwrite them, and takes the feed once idle again.
describe("RunQueueColumn shadow convergence", () => {
  it("holds the shadow queue when a poll lands mid-start-next", async () => {
    let release;
    const gate = new Promise((r) => (release = r));
    const handlers = useQueueHandlers({
      fetchFn: vi.fn(() => gate.then(() => ({ ok: true, status: 200 }))),
      ...makeReloads(),
    });

    const feed = ref([{ id: "q1" }, { id: "q2" }]);
    const shadow = useQueueFeedShadow({
      feed: () => feed.value,
      isSaving: handlers.isSaving,
      onReorder: handlers.reorder,
    });

    const pending = handlers.startNext({ id: "q1" });
    await new Promise((r) => setTimeout(r, 0));
    expect(handlers.isStartingNext.value).toBe(true);
    expect(handlers.isSaving.value).toBe(true);

    feed.value = [{ id: "q9" }];
    await nextTick();
    expect(shadow.localQueue.value.map((i) => i.id)).toEqual(["q1", "q2"]);

    release();
    await pending;

    feed.value = [{ id: "q9" }];
    await nextTick();
    expect(shadow.localQueue.value.map((i) => i.id)).toEqual(["q9"]);
  });
});

// The column's handlers are the shared composable itself (Block A):
// RunQueueColumn wires useQueueHandlers with its two feed loads, so the
// write-then-resync contract — run the write, then refresh the feeds that
// show its effect — is exercised here against that same composable with a
// fake fetch and fake loads. No wiring is re-stated: the handler logic lives
// once in services/useQueueHandlers.js.
describe("RunQueueColumn write-then-resync", () => {
  it("reloads both feeds after a successful remove", async () => {
    const reloads = makeReloads();
    const { remove } = useQueueHandlers({
      fetchFn: vi.fn(() => Promise.resolve({ ok: true, status: 200 })),
      ...reloads,
    });

    await remove({ id: "q1" });

    expect(reloads.reloadIssues).toHaveBeenCalledTimes(1);
    expect(reloads.reloadQueue).toHaveBeenCalledTimes(1);
  });

  it("reloads both feeds concurrently after a successful remove", async () => {
    let release;
    const gate = new Promise((r) => (release = r));
    const reloadIssues = vi.fn(() => gate);
    const reloadQueue = vi.fn(() => Promise.resolve());
    const { remove } = useQueueHandlers({
      fetchFn: vi.fn(() => Promise.resolve({ ok: true, status: 200 })),
      reloadIssues,
      reloadQueue,
    });

    const pending = remove({ id: "q1" });
    await new Promise((r) => setTimeout(r, 0));

    // Promise.all: both reloads start before either finishes.
    expect(reloadIssues).toHaveBeenCalledTimes(1);
    expect(reloadQueue).toHaveBeenCalledTimes(1);
    release();
    await pending;
    expect(reloadIssues).toHaveBeenCalledTimes(1);
    expect(reloadQueue).toHaveBeenCalledTimes(1);
  });

  it("reloads nothing and records the error when a remove fails", async () => {
    const reloads = makeReloads();
    const { remove, removeError } = useQueueHandlers({
      fetchFn: vi.fn(() => Promise.resolve({ ok: false, status: 500 })),
      ...reloads,
    });

    await remove({ id: "q1" });

    expect(reloads.reloadIssues).not.toHaveBeenCalled();
    expect(reloads.reloadQueue).not.toHaveBeenCalled();
    expect(removeError.value).toBe("HTTP 500");
  });

  it("reloads only the queue after a successful start-next", async () => {
    const reloads = makeReloads();
    const { startNext } = useQueueHandlers({
      fetchFn: vi.fn(() => Promise.resolve({ ok: true, status: 200 })),
      ...reloads,
    });

    await startNext({ id: "q1" });

    expect(reloads.reloadQueue).toHaveBeenCalledTimes(1);
    expect(reloads.reloadIssues).not.toHaveBeenCalled();
  });

  it("reloads only the queue after a successful restart", async () => {
    const reloads = makeReloads();
    const { restart } = useQueueHandlers({
      fetchFn: vi.fn(() => Promise.resolve({ ok: true, status: 200 })),
      ...reloads,
    });

    await restart({ id: "q2" });

    expect(reloads.reloadQueue).toHaveBeenCalledTimes(1);
    expect(reloads.reloadIssues).not.toHaveBeenCalled();
  });

  it("reloads the queue after a successful reorder", async () => {
    const reloads = makeReloads();
    const { reorder } = useQueueHandlers({
      fetchFn: vi.fn(() => Promise.resolve({ ok: true, status: 200 })),
      ...reloads,
    });

    await reorder(["q2", "q1"]);

    expect(reloads.reloadQueue).toHaveBeenCalledTimes(1);
    expect(reloads.reloadIssues).not.toHaveBeenCalled();
  });

  it("still re-syncs the queue after a failed reorder persist", async () => {
    const reloads = makeReloads();
    const { reorder, reorderError } = useQueueHandlers({
      fetchFn: vi.fn(() => Promise.resolve({ ok: false, status: 500 })),
      ...reloads,
    });

    await reorder(["q2", "q1"]);

    // Unconditional re-sync: the shadow copy must converge on server truth
    // even when the persist failed, so the queue reload still fires.
    expect(reloads.reloadQueue).toHaveBeenCalledTimes(1);
    expect(reloads.reloadIssues).not.toHaveBeenCalled();
    expect(reorderError.value).toBe("HTTP 500");
  });
});
