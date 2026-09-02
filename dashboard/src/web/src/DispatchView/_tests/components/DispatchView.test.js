import { describe, it, expect } from "vitest";
import { createSSRApp, h } from "vue";
import { renderToString } from "vue/server-renderer";
import DispatchView from "../../components/DispatchView.vue";

// Rendered via SSR (mirroring ErrorBanner's test) so the composition layer's
// contract can be asserted without a DOM. Feeds only load on mount, which SSR
// never runs, so the orchestrator renders deterministically with empty feeds:
// both columns composed, each telling its empty-state story, plus the panel
// header with its host badge.
async function renderView() {
  const app = createSSRApp({ render: () => h(DispatchView) });
  return renderToString(app);
}

describe("DispatchView composition", () => {
  it("renders the panel header and composes both columns with their empty states", async () => {
    const html = await renderView();

    expect(html).toContain("Dispatch");
    expect(html).toContain("host offline");
    expect(html).toContain("Eligible Issues");
    expect(html).toContain("No eligible issues synced yet.");
    expect(html).toContain("Run Queue");
    expect(html).toContain("The queue is empty.");
  });
});
