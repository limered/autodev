import { describe, it, expect } from "vitest";
import { createSSRApp, h } from "vue";
import { renderToString } from "vue/server-renderer";
import EligibleIssuesColumn from "../../components/EligibleIssuesColumn.vue";

// Rendered via SSR (mirroring ErrorBanner's test) so the column's markup
// contract can be asserted without a DOM: which issues count as eligible,
// what the empty states say, and which banners/disabled states render.
async function renderColumn(props) {
  const app = createSSRApp({ render: () => h(EligibleIssuesColumn, props) });
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

  it("shows the sync and enqueue error banners", async () => {
    const html = await renderColumn({
      issues: [],
      syncError: "HTTP 500",
      enqueueError: "HTTP 502",
    });

    expect(html).toContain("Sync failed");
    expect(html).toContain("Failed to load eligible issues: HTTP 500");
    expect(html).toContain("Enqueue failed");
    expect(html).toContain("HTTP 502");
  });

  it("disables the enqueue buttons while an enqueue is in flight", async () => {
    const enabled = await renderColumn({ issues: [issue(101, 12)], isEnqueueing: false });
    const disabled = await renderColumn({ issues: [issue(101, 12)], isEnqueueing: true });

    expect(enabled).not.toContain('class="enqueue-button" disabled');
    expect(disabled).toContain('class="enqueue-button" disabled');
  });
});
