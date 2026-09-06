import { describe, it, expect } from "vitest";
import { createSSRApp, h } from "vue";
import { renderToString } from "vue/server-renderer";
import RunSection from "../../components/RunSection.vue";

// Rendered via SSR (mirroring ErrorBanner's test) so the shell contract can be
// asserted without a DOM: header markup, alert placement, and the idle gate.
function renderSection(props, slots) {
  return renderToString(createSSRApp({ render: () => h(RunSection, props, slots) }));
}

const rows = () => h("div", { class: "run-list" }, "rows");
const banner = () =>
  h("section", { class: "error-banner", role: "alert" }, "boom");

describe("RunSection", () => {
  it("renders the labelled section with prompt header and default slot", async () => {
    const html = await renderSection(
      { title: "Active", ariaLabel: "Active runs" },
      { default: rows },
    );

    expect(html).toContain('aria-label="Active runs"');
    expect(html).toContain('class="section-header"');
    expect(html).toMatch(/class="prompt"[^>]*>&gt;<\/span>/);
    expect(html).toContain("Active</h2>");
    expect(html).toContain('class="run-list"');
  });

  it("renders alerts above the section", async () => {
    const html = await renderSection(
      { title: "History", ariaLabel: "Run history" },
      { alerts: banner, default: rows },
    );

    const bannerAt = html.indexOf("error-banner");
    const sectionAt = html.indexOf('aria-label="Run history"');
    expect(bannerAt).toBeGreaterThan(-1);
    expect(sectionAt).toBeGreaterThan(-1);
    expect(bannerAt).toBeLessThan(sectionAt);
  });

  it("hides the section but keeps alerts while the active list is idle", async () => {
    const html = await renderSection(
      { title: "Active", ariaLabel: "Active runs", hideSection: true },
      { alerts: banner, default: rows },
    );

    expect(html).toContain("error-banner");
    expect(html).not.toContain('aria-label="Active runs"');
    expect(html).not.toContain("section-header");
  });
});
