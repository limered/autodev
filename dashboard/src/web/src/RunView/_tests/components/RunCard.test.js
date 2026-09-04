import { describe, it, expect } from "vitest";
import { createSSRApp, h } from "vue";
import { renderToString } from "vue/server-renderer";
import RunCard from "../../components/RunCard.vue";

// Rendered via SSR (mirroring ErrorBanner's test) so the card's markup
// contract can be asserted without a DOM. The Variant C header (issue #99)
// is the point of these tests: the meta line (status pill · repo/branch ·
// abort) is the whole header, and the stage strip is a separate full-width
// band below the header divider — pills and stages never share a flex row,
// so a wrapping strip can never push the pill or the abort control around.
// The delete click → emit wiring needs a DOM event lifecycle this harness
// doesn't have (see RunQueueColumn.test.js); the abort control's contract
// here is its markup: × glyph, title/aria-label, and `deletable` gating.

// Fixed clock so renders are deterministic; freshness maths is covered at
// the runView seam.
const NOW = new Date("2026-09-04T12:00:00Z").getTime();

const stage = (agent, status) => ({ agent, model: "glm-5.2", status });

const run = (overrides = {}) => ({
  runId: "run-1",
  repo: "owner/repo",
  branch: "main",
  status: "running",
  startedAt: "2026-09-04T11:00:00Z",
  lastHeartbeatAt: "2026-09-04T11:59:50Z",
  ...overrides,
});

async function renderCard(r, props = {}) {
  const app = createSSRApp({ render: () => h(RunCard, { run: r, now: NOW, ...props }) });
  return renderToString(app);
}

// Locates the first <tag> element carrying cls and returns its inner HTML
// plus tag offsets, so nesting and order can be asserted without a DOM.
// Tracks same-tag depth while scanning; Vue SSR never emits self-closing
// divs/uls/buttons, so open/close pairing is unambiguous.
function findElement(html, tag, cls) {
  const open = new RegExp(`<${tag}\\b[^>]*\\bclass="[^"]*\\b${cls}\\b[^"]*"[^>]*>`).exec(html);
  if (!open) return null;
  const innerStart = open.index + open[0].length;
  const pair = new RegExp(`<${tag}\\b[^>]*>|</${tag}>`, "g");
  pair.lastIndex = innerStart;
  let depth = 1;
  let m;
  while ((m = pair.exec(html))) {
    depth += m[0][1] === "/" ? -1 : 1;
    if (depth === 0) {
      return { inner: html.slice(innerStart, m.index), openStart: open.index, closeStart: m.index };
    }
  }
  return null;
}

describe("RunCard header (Variant C: meta line + stage band)", () => {
  it("keeps the meta line to status pill, repo/branch, then abort control", async () => {
    const html = await renderCard(run({ stages: [stage("triage", "done")] }), {
      deletable: true,
    });
    const header = findElement(html, "div", "card-header");

    expect(header).not.toBeNull();
    expect(header.inner.indexOf("status-badge")).toBeGreaterThanOrEqual(0);
    expect(header.inner.indexOf("status-badge")).toBeLessThan(
      header.inner.indexOf('class="repo-branch"'),
    );
    expect(header.inner.indexOf('class="repo-branch"')).toBeLessThan(
      header.inner.indexOf("delete-run"),
    );
    expect(header.inner).toContain("owner/repo");
    expect(header.inner).toContain("main");
  });

  it("never nests the stage strip inside the meta line's flex row", async () => {
    const html = await renderCard(
      run({ stages: [stage("triage", "done"), stage("build", "running")] }),
    );
    const header = findElement(html, "div", "card-header");

    expect(header).not.toBeNull();
    expect(header.inner).not.toContain("stage-strip");
  });

  it("renders the stage strip as its own band below the header, above the body", async () => {
    const html = await renderCard(
      run({ stages: [stage("triage", "done"), stage("build", "running")] }),
    );
    const header = findElement(html, "div", "card-header");
    const strip = findElement(html, "ul", "stage-strip");

    expect(strip).not.toBeNull();
    // Opens after the header element closes → below the header divider, on
    // its own row rather than inside the meta flex row.
    expect(strip.openStart).toBeGreaterThan(header.closeStart);
    expect(strip.closeStart).toBeLessThan(html.indexOf('class="card-body"'));
  });

  it("keeps the meta line byte-identical as stage counts change, so the pill cannot shift", async () => {
    const noStages = await renderCard(run());
    const few = await renderCard(
      run({ stages: [stage("triage", "done"), stage("build", "running")] }),
    );
    const many = await renderCard(
      run({
        stages: ["a", "b", "c", "d", "e", "f", "g", "h"].map((agent) => stage(agent, "pending")),
      }),
    );
    const metaLine = (html) => findElement(html, "div", "card-header").inner;

    expect(metaLine(few)).toBe(metaLine(noStages));
    expect(metaLine(many)).toBe(metaLine(noStages));
  });

  it("renders the abort control as a glyph-only × carrying title and aria-label", async () => {
    const html = await renderCard(run(), { deletable: true });
    const button = html.match(/<button[^>]*\bdelete-run\b[^>]*>[\s\S]*?<\/button>/)?.[0] ?? "";

    expect(button).not.toBe("");
    // Whitespace-stripped content: the × glyph only, no "Delete" text label.
    expect(button.replace(/<[^>]*>/g, "").trim()).toBe("×");
    expect(button).toContain('title="Delete this run"');
    expect(button).toContain('aria-label="Delete this run"');
  });

  it("omits the abort control entirely for non-deletable cards", async () => {
    const html = await renderCard(run());

    expect(html).not.toContain("delete-run");
  });
});

describe("RunCard body and status (unchanged by the header rework)", () => {
  it("renders the stat rows with the live Last seen for active runs", async () => {
    const html = await renderCard(run());

    expect(html).toContain("Last seen");
    expect(html).toContain("10s ago");
    expect(html).toContain("Started");
  });

  it("renders a fixed Completed label for terminal runs", async () => {
    const html = await renderCard(run({ status: "done", finishedAt: "2026-09-04T11:30:00Z" }));

    expect(html).toContain("Completed");
    expect(html).toContain(new Date("2026-09-04T11:30:00Z").toLocaleString());
  });

  it("carries the status colour class on the card and the pill", async () => {
    const html = await renderCard(run({ status: "failed" }));
    // The pill is the div whose class list carries the status colour (SSR
    // merges the static and dynamic classes in either order).
    const pill = findElement(html, "div", "status-failed");

    expect(html).toContain('class="run-card status-failed"');
    expect(pill).not.toBeNull();
    expect(pill.inner).toContain("status-indicator");
  });

  it("renders each stage with its status colour class", async () => {
    const html = await renderCard(
      run({
        stages: [stage("triage", "done"), stage("build", "running"), stage("review", "pending")],
      }),
    );

    expect(html).toContain("stage-done");
    expect(html).toContain("stage-running");
    expect(html).toContain("stage-pending");
    expect(html).toContain("triage");
    expect(html).toContain("glm-5.2");
  });

  it("renders secondary rows: PR link, failure reason, freeze snapshot", async () => {
    const html = await renderCard(
      run({
        status: "failed",
        failureReason: "agent crashed",
        prUrl: "https://github.com/limered/autodev/pull/99",
        freezeCaptured: true,
        freezeLocalPath: "/snapshots/run-1",
      }),
    );

    expect(html).toContain('href="https://github.com/limered/autodev/pull/99"');
    expect(html).toContain("agent crashed");
    expect(html).toContain("/snapshots/run-1");
  });

  it("shows the VM stat when the run names a VM", async () => {
    const html = await renderCard(run({ vmName: "vm-42" }));

    expect(html).toContain("VM");
    expect(html).toContain("vm-42");
  });
});
