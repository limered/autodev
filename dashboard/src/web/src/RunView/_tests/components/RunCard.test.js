import { describe, it, expect } from "vitest";
import { createSSRApp, h } from "vue";
import { renderToString } from "vue/server-renderer";
import RunCard from "../../components/RunCard.vue";

// Rendered via SSR (mirroring ErrorBanner's test) so the card's markup
// contract can be asserted without a DOM. The Variant C header (issue #99)
// is the point of these tests: the meta line (status pill · repo/branch ·
// abort) is the whole header. Run detail lives only in the expandable
// dev-loop section. The delete click → emit wiring needs a DOM event
// lifecycle this harness doesn't have (see RunQueueColumn.test.js); the
// abort control's contract here is its markup: × glyph, title/aria-label,
// and `deletable` gating.

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

describe("RunCard header (Variant C: meta line)", () => {
  it("keeps the meta line to status pill, repo/branch, then abort control", async () => {
    const html = await renderCard(run(), {
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

describe("RunCard dev-loop detail (C3 stepped rail)", () => {
  const apiStep = (overrides = {}) => ({
    agent: "feature-builder",
    iteration: 0,
    model: "m1",
    status: "done",
    inputTokens: 18200,
    outputTokens: 6400,
    durationMs: 552000,
    cost: null,
    ...overrides,
  });

  const finishedSteps = () => [
    apiStep(),
    apiStep({
      agent: "test-runner",
      model: "m2",
      inputTokens: 9400,
      outputTokens: 1800,
      durationMs: 220000,
    }),
    apiStep({
      agent: "static-analysis",
      iteration: 1,
      model: "m3",
      inputTokens: 6100,
      outputTokens: 900,
      durationMs: 68000,
    }),
    apiStep({
      agent: "feature-builder",
      iteration: 1,
      model: "m1",
      inputTokens: 3000,
      outputTokens: 1500,
      durationMs: 90000,
    }),
  ];

  it("offers the expandable detail with step count and totals, collapsed by default", async () => {
    const html = await renderCard(run({ steps: finishedSteps() }));

    expect(html).toContain("dev-loop-toggle");
    expect(html).toContain("show dev-loop detail");
    expect(html).toContain("4 steps");
    expect(html).not.toContain("dev-loop-item");
  });

  it("expands to every phase plus each loop iteration with tokens and duration", async () => {
    const html = await renderCard(run({ steps: finishedSteps() }), { initialExpanded: true });
    const list = findElement(html, "ol", "dev-loop");

    expect(list).not.toBeNull();
    expect(html).toContain("feature-builder");
    expect(html).toContain("test-runner");
    expect(html).toContain("static-analysis");
    expect(html).toContain("m1");
    expect(html).toContain("m2");
    expect((html.match(/dev-loop-item/g) ?? []).length).toBeGreaterThanOrEqual(4);
  });

  it("badges the iteration only when the agent repeats", async () => {
    const html = await renderCard(run({ steps: finishedSteps() }), { initialExpanded: true });

    expect(html).toContain("dev-loop-iter");
    expect(html).not.toContain("#0");
    expect(html).toContain("#1");
  });

  it("keeps the rail styling: spine dots, step arrows, loop indent and loop-back badge", async () => {
    const html = await renderCard(run({ steps: finishedSteps() }), { initialExpanded: true });

    expect(html).toContain("dev-loop-rail");
    expect(html).toContain("dev-loop-dot");
    expect(html).toContain("dev-loop-arrow");
    expect(html).toContain("dev-loop-is-loop");
    expect(html).toContain("↺");
    expect(html).toContain("stage-done");
  });

  it("falls back to seeded stages when steps are empty", async () => {
    const html = await renderCard(run({ stages: [stage("triage", "done")], steps: [] }), {
      initialExpanded: true,
    });

    expect(html).toContain("dev-loop-toggle");
    expect(html).toContain("triage");
    expect(html).toContain("glm-5.2");
  });

  it("omits the detail section when neither steps nor stages exist", async () => {
    const html = await renderCard(run());

    expect(html).not.toContain("dev-loop-toggle");
    expect(html).not.toContain("dev-loop-item");
  });

  it("fills the toggle strip fully when every step is terminal", async () => {
    const html = await renderCard(run({ steps: finishedSteps() }));

    expect(html).toContain("linear-gradient");
    expect(html).toContain("100%");
    expect(html).toContain("4 steps");
  });

  it("fills the toggle strip partially while steps are still running", async () => {
    const steps = [
      apiStep(),
      apiStep({ agent: "test-runner", model: "m2", status: "running" }),
      apiStep({ agent: "static-analysis", iteration: 1, model: "m3", status: "pending" }),
      apiStep({ agent: "feature-builder", iteration: 1, model: "m1", status: "running" }),
    ];
    const html = await renderCard(run({ steps }));

    expect(html).toContain("linear-gradient");
    expect(html).toContain("25%");
    expect(html).toContain("4 steps");
  });

  it("keeps the fill on the toggle when expanded", async () => {
    const html = await renderCard(run({ steps: finishedSteps() }), { initialExpanded: true });

    expect(html).toContain("dev-loop-toggle");
    expect(html).toContain("linear-gradient");
    expect(html).toContain("100%");
    expect(html).toContain("hide dev-loop detail");
  });
});

describe("RunCard grouped detail (categories)", () => {
  const categorizedStages = (status = "running") => [
    { agent: "implementation", category: "implementation", model: "m-impl", status },
    { agent: "quality-loop", category: "quality-loop", model: "m-loop", status: "pending" },
    { agent: "test-rerun", category: "test-rerun", model: "m-test", status: "pending" },
  ];

  const groupedSteps = () => [
    {
      agent: "feature-builder",
      category: "implementation",
      iteration: 0,
      model: "m-impl",
      status: "done",
      inputTokens: 18200,
      outputTokens: 6400,
      durationMs: 552000,
      cost: null,
    },
    {
      agent: "test-runner",
      category: "implementation",
      iteration: 0,
      model: "m-test",
      status: "done",
      inputTokens: 9400,
      outputTokens: 1800,
      durationMs: 220000,
      cost: null,
    },
    {
      agent: "static-analysis",
      category: "quality-loop",
      iteration: 1,
      model: "m-loop",
      status: "done",
      inputTokens: 6100,
      outputTokens: 900,
      durationMs: 68000,
      cost: null,
    },
    {
      agent: "feature-builder",
      category: "quality-loop",
      iteration: 1,
      model: "m-impl",
      status: "done",
      inputTokens: 3000,
      outputTokens: 1500,
      durationMs: 90000,
      cost: null,
    },
  ];

  it("shows category dots only while live: headers for each category, no per-worker rows", async () => {
    const html = await renderCard(
      run({ status: "running", stages: categorizedStages(), steps: [] }),
      { initialExpanded: true },
    );

    expect(html).toContain("implementation");
    expect(html).toContain("quality-loop");
    expect(html).toContain("stage-running");
    // No per-worker detail has landed: the loop workers never appear, and no
    // group headers render while there is nothing to group.
    expect(html).not.toContain("static-analysis");
    expect(html).not.toContain("dev-loop-group");
    expect(html).toContain("hide dev-loop detail");
  });

  it("groups finished per-worker rows under their category header", async () => {
    const html = await renderCard(
      run({
        status: "done",
        stages: categorizedStages("done"),
        steps: groupedSteps(),
      }),
      { initialExpanded: true },
    );
    const list = findElement(html, "ol", "dev-loop");

    expect(list).not.toBeNull();
    expect(html).toContain("dev-loop-group");
    // The quality-loop header precedes its member rows in the markup.
    const headerAt = list.inner.indexOf("dev-loop-group");
    const memberAt = list.inner.indexOf("static-analysis");
    expect(headerAt).toBeGreaterThanOrEqual(0);
    expect(memberAt).toBeGreaterThan(headerAt);
    // Both implementation members render.
    expect(list.inner).toContain("feature-builder");
    expect(list.inner).toContain("test-runner");
    // The toggle still counts worker rows with unchanged totals.
    expect(html).toContain("4 steps");
  });

  it("keeps the loop affordance on grouped rows", async () => {
    const html = await renderCard(
      run({
        status: "done",
        stages: categorizedStages("done"),
        steps: groupedSteps(),
      }),
      { initialExpanded: true },
    );

    expect(html).toContain("dev-loop-is-loop");
    expect(html).toContain("↺");
    expect(html).toContain("#1");
    expect(html).not.toContain("#0");
  });

  it("groups steps without a category under the uncategorized bucket", async () => {
    const html = await renderCard(
      run({
        status: "done",
        stages: [{ agent: "feature-builder", model: "m-impl", status: "done" }],
        steps: [
          {
            agent: "feature-builder",
            category: "implementation",
            iteration: 0,
            model: "m-impl",
            status: "done",
            inputTokens: 100,
            outputTokens: 50,
            durationMs: 1000,
            cost: null,
          },
          {
            agent: "ghost",
            iteration: 0,
            model: "m-ghost",
            status: "done",
            inputTokens: 100,
            outputTokens: 50,
            durationMs: 1000,
            cost: null,
          },
        ],
      }),
      { initialExpanded: true },
    );

    expect(html).toContain("dev-loop-group");
    expect(html).toContain("uncategorized");
    expect(html).toContain("feature-builder");
    expect(html).toContain("ghost");
  });

  it("hides empty category headers once steps have landed elsewhere", async () => {
    const html = await renderCard(
      run({
        status: "done",
        stages: categorizedStages("done"),
        steps: [
          {
            agent: "feature-builder",
            category: "implementation",
            iteration: 0,
            model: "m-impl",
            status: "done",
            inputTokens: 100,
            outputTokens: 50,
            durationMs: 1000,
            cost: null,
          },
          {
            agent: "ghost",
            iteration: 0,
            model: "m-ghost",
            status: "done",
            inputTokens: 100,
            outputTokens: 50,
            durationMs: 1000,
            cost: null,
          },
        ],
      }),
      { initialExpanded: true },
    );
    const list = findElement(html, "ol", "dev-loop");

    expect(list).not.toBeNull();
    expect(list.inner).toContain("implementation");
    expect(list.inner).toContain("uncategorized");
    expect(list.inner).toContain("feature-builder");
    expect(list.inner).toContain("ghost");
    expect(list.inner).not.toContain("quality-loop");
    expect(list.inner).not.toContain("test-rerun");
  });

  it("renders a lone populated group flat without its header", async () => {
    const html = await renderCard(
      run({
        status: "done",
        stages: categorizedStages("done"),
        steps: [
          {
            agent: "feature-builder",
            iteration: 0,
            model: "m-impl",
            status: "done",
            inputTokens: 100,
            outputTokens: 50,
            durationMs: 1000,
            cost: null,
          },
          {
            agent: "test-runner",
            iteration: 0,
            model: "m-test",
            status: "done",
            inputTokens: 100,
            outputTokens: 50,
            durationMs: 1000,
            cost: null,
          },
        ],
      }),
      { initialExpanded: true },
    );
    const list = findElement(html, "ol", "dev-loop");

    expect(list).not.toBeNull();
    expect(list.inner).not.toContain("dev-loop-group");
    expect(list.inner).not.toContain("uncategorized");
    expect(list.inner).toContain("feature-builder");
    expect(list.inner).toContain("test-runner");
    expect(html).toContain("2 steps");
  });
});
