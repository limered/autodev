import { describe, it, expect } from "vitest";
import { createSSRApp, h } from "vue";
import { renderToString } from "vue/server-renderer";
import RunQueueColumn from "../../components/RunQueueColumn.vue";

// Rendered via SSR (mirroring ErrorBanner's test) so the column's markup
// contract can be asserted without a DOM: row order, status badges, the
// restart/remove controls, Start-next gating, banners, and the empty state.
// The drag-reorder math itself is covered at the useDragReorder seam.
async function renderColumn(props) {
  const app = createSSRApp({ render: () => h(RunQueueColumn, props) });
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
  it("renders the queue in order with rank, issue, and status badge per row", async () => {
    const html = await renderColumn({
      queue: [
        queued("q1", 1, "First task"),
        queued("q2", 2, "Second task", { runId: "r2", runStatus: "running" }),
      ],
    });

    expect(html.indexOf("First task")).toBeLessThan(html.indexOf("Second task"));
    expect(html).toContain("#1");
    expect(html).toContain("#2");
    expect(html).toContain("status-queued");
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

  it("labels Start next as busy and does not call it a save while starting", async () => {
    const html = await renderColumn({
      queue: [queued("q1", 1, "First task")],
      isStartingNext: true,
    });

    expect(html).toContain("Starting…");
    expect(html).not.toContain("saving…");
  });

  it("shows the saving indicator while a queue save is in flight", async () => {
    const reordering = await renderColumn({
      queue: [queued("q1", 1, "First task")],
      isReordering: true,
    });

    expect(reordering).toContain("saving…");
  });

  it("shows a banner per queue error", async () => {
    const html = await renderColumn({
      queue: [],
      syncError: "HTTP 500",
      reorderError: "HTTP 501",
      removeError: "HTTP 502",
      startNextError: "HTTP 503",
      restartError: "HTTP 504",
    });

    expect(html).toContain("Failed to load run queue: HTTP 500");
    expect(html).toContain("Reorder failed");
    expect(html).toContain("Remove failed");
    expect(html).toContain("Start failed");
    expect(html).toContain("Restart failed");
  });

  it("shows the empty state when the queue is empty", async () => {
    const html = await renderColumn({ queue: [] });

    expect(html).toContain("The queue is empty.");
  });
});
