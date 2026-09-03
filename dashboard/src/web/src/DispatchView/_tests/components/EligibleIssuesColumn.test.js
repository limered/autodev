import { describe, it, expect, vi } from "vitest";
import { createSSRApp, h } from "vue";
import { renderToString } from "vue/server-renderer";
import EligibleIssuesColumn from "../../components/EligibleIssuesColumn.vue";
import { useQueueActions } from "../../services/useQueueActions.js";

// Rendered via SSR (mirroring ErrorBanner's test) so the column's markup
// contract can be asserted without a DOM: which issues count as eligible,
// what the empty states say, and which banners render. The enqueue write
// and its busy/error state live inside the column now (issue #75), so their
// markup (disabled-while-enqueueing, enqueue-error banner) is beyond this
// harness — the write-then-resync contract is covered at the composable seam
// in the describe below.
async function renderColumn(props) {
  const app = createSSRApp({
    render: () =>
      h(EligibleIssuesColumn, { reloadIssues: vi.fn(), reloadQueue: vi.fn(), ...props }),
  });
  return renderToString(app);
}

const issue = (gitHubId, number) => ({
  gitHubId,
  number,
  title: `Fix thing ${number}`,
  htmlUrl: `https://example.com/issues/${number}`,
  repo: "owner/repo",
});

describe("EligibleIssuesColumn", () => {
  it("renders only the issues that are not already in the run queue", async () => {
    const html = await renderColumn({
      issues: [issue(101, 12), issue(102, 13)],
      queuedIssueIds: new Set([102]),
    });

    expect(html).toContain("Fix thing 12");
    expect(html).toContain("#12");
    expect(html).not.toContain("Fix thing 13");
    expect(html).toContain("Enqueue →");
  });

  it("says all eligible issues are queued when the feed has issues but none are eligible", async () => {
    const html = await renderColumn({
      issues: [issue(101, 12)],
      queuedIssueIds: new Set([101]),
    });

    expect(html).toContain("All eligible issues are queued.");
    expect(html).not.toContain("No eligible issues synced yet.");
  });

  it("says no issues have synced yet when the feed is empty", async () => {
    const html = await renderColumn({ issues: [], queuedIssueIds: new Set() });

    expect(html).toContain("No eligible issues synced yet.");
  });

  it("shows the sync error banner", async () => {
    const html = await renderColumn({
      issues: [],
      syncError: "HTTP 500",
    });

    expect(html).toContain("Sync failed");
    expect(html).toContain("Failed to load eligible issues: HTTP 500");
  });
});

// The column's enqueue handler composes useQueueActions with the two feed
// loads exactly as wired in EligibleIssuesColumn.vue. Buttons need a DOM
// event lifecycle this SSR harness doesn't have, so the write-then-resync
// contract — run the write, then refresh both feeds it affects — is
// exercised here at the composable seam with a fake fetch and fake loads
// (mirroring RunsList.test.js's makeWiredLists). Keep this wiring in
// lockstep with the component's onEnqueue.
function makeWiredColumn({ writeFetch, reloadIssues, reloadQueue }) {
  const actions = useQueueActions(writeFetch);
  const enqueue = async (issue) => {
    if (await actions.enqueue(issue.gitHubId)) {
      await Promise.all([reloadIssues(), reloadQueue()]);
    }
  };
  return { actions, enqueue };
}

describe("EligibleIssuesColumn write-then-resync", () => {
  it("reloads both feeds after a successful enqueue", async () => {
    const reloadIssues = vi.fn();
    const reloadQueue = vi.fn();
    const { enqueue } = makeWiredColumn({
      writeFetch: vi.fn(() => Promise.resolve({ ok: true, status: 201 })),
      reloadIssues,
      reloadQueue,
    });

    await enqueue(issue(101, 12));

    expect(reloadIssues).toHaveBeenCalledTimes(1);
    expect(reloadQueue).toHaveBeenCalledTimes(1);
  });

  it("reloads nothing and records the error when the enqueue fails", async () => {
    const reloadIssues = vi.fn();
    const reloadQueue = vi.fn();
    const { actions, enqueue } = makeWiredColumn({
      writeFetch: vi.fn(() => Promise.resolve({ ok: false, status: 503 })),
      reloadIssues,
      reloadQueue,
    });

    await enqueue(issue(101, 12));

    expect(reloadIssues).not.toHaveBeenCalled();
    expect(reloadQueue).not.toHaveBeenCalled();
    expect(actions.enqueueError.value).toBe("HTTP 503");
  });
});
