/**
 * @vitest-environment happy-dom
 */
import { describe, it, expect } from "vitest";
import { createSSRApp, createApp, h, nextTick } from "vue";
import { renderToString } from "vue/server-renderer";
import WorkflowPicker from "../../components/WorkflowPicker.vue";

const workflows = [
  { name: "full", stageCount: 5 },
  { name: "quick", stageCount: 2 },
];

async function renderPicker(props) {
  const app = createSSRApp({ render: () => h(WorkflowPicker, props) });
  return renderToString(app);
}

const ready = { kind: "ready", name: "full", stageCount: 5 };

describe("WorkflowPicker", () => {
  it("renders a plain-text trigger with workflow name plus stage count", async () => {
    const html = await renderPicker({ state: ready, workflows });

    expect(html).toContain("workflow-trigger");
    expect(html).toContain("full");
    expect(html).toContain("5");
    expect(html).not.toContain("workflow:");
    expect(html).not.toContain("implementation");
  });

  it("renders one menu row per workflow with checkmark, name, and muted count", async () => {
    const html = await renderPicker({ state: ready, workflows, initialOpen: true });

    expect(html).toContain("workflow-menu");
    expect(html).toContain("✓");
    expect(html).toContain("quick");
    expect(html).toContain("2");
    expect(html).not.toContain("implementation");
  });

  it("keeps the menu closed until the trigger opens it", async () => {
    const html = await renderPicker({ state: ready, workflows });

    expect(html).not.toContain("workflow-menu");
  });

  it("renders plain frozen text with no trigger on claimed rows", async () => {
    const html = await renderPicker({
      state: { kind: "frozen", name: "quick", stageCount: 2 },
      workflows,
    });

    expect(html).toContain("quick");
    expect(html).toContain("frozen");
    expect(html).not.toContain("workflow-trigger");
    expect(html).not.toContain("workflow-menu");
  });

  it("renders the muted default notice with no trigger when the catalog is missing", async () => {
    const html = await renderPicker({
      state: { kind: "missing", name: "default", stageCount: 0 },
      workflows: [],
    });

    expect(html).toContain("default (no catalog)");
    expect(html).not.toContain("workflow-trigger");
  });
});

function mountPicker(props) {
  const calls = [];
  const el = document.createElement("div");
  document.body.appendChild(el);
  const app = createApp({
    render: () => h(WorkflowPicker, { ...props, onPick: (name) => calls.push(name) }),
  });
  app.mount(el);
  return { el, app, calls };
}

describe("WorkflowPicker interaction", () => {
  it("opens the menu on trigger click and emits the picked workflow", async () => {
    const { el, app, calls } = mountPicker({ state: ready, workflows });

    el.querySelector(".workflow-trigger").click();
    await nextTick();
    expect(el.querySelector(".workflow-menu")).not.toBe(null);

    el.querySelectorAll(".workflow-option")[1].click();
    await nextTick();
    expect(calls).toEqual(["quick"]);
    expect(el.querySelector(".workflow-menu")).toBe(null);

    app.unmount();
    el.remove();
  });

  it("closes an open menu on Escape", async () => {
    const { el, app } = mountPicker({ state: ready, workflows });

    el.querySelector(".workflow-trigger").click();
    await nextTick();
    expect(el.querySelector(".workflow-menu")).not.toBe(null);

    el.querySelector(".workflow-trigger").dispatchEvent(
      new KeyboardEvent("keyup", { key: "Escape", bubbles: true }),
    );
    await nextTick();
    expect(el.querySelector(".workflow-menu")).toBe(null);

    app.unmount();
    el.remove();
  });

  it("closes an open menu on outside click", async () => {
    const { el, app } = mountPicker({ state: ready, workflows });

    el.querySelector(".workflow-trigger").click();
    await nextTick();
    expect(el.querySelector(".workflow-menu")).not.toBe(null);

    document.body.dispatchEvent(new MouseEvent("click", { bubbles: true }));
    await nextTick();
    expect(el.querySelector(".workflow-menu")).toBe(null);

    app.unmount();
    el.remove();
  });
});
