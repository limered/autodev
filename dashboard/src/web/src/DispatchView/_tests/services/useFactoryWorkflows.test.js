import { describe, it, expect, vi } from "vitest";
import { useFactoryWorkflows } from "../../services/useFactoryWorkflows.js";

const catalog = {
  defaultWorkflow: "full",
  workflows: [
    { name: "full", stageCount: 5 },
    { name: "quick", stageCount: 2 },
  ],
};

function okJson(payload) {
  return () => Promise.resolve({ ok: true, status: 200, json: () => Promise.resolve(payload) });
}

describe("useFactoryWorkflows", () => {
  it("loads the factory catalog onto the catalog ref", async () => {
    const feed = useFactoryWorkflows(okJson(catalog));

    await feed.load();

    expect(feed.catalog.value).toEqual(catalog);
    expect(feed.error.value).toBe(null);
    expect(feed.isLoading.value).toBe(false);
  });

  it("records the error and keeps a null catalog when the fetch fails", async () => {
    const feed = useFactoryWorkflows(() => Promise.resolve({ ok: false, status: 500 }));

    await feed.load();

    expect(feed.catalog.value).toBe(null);
    expect(feed.error.value).toBe("HTTP 500");
  });

  it("records the error when the fetch rejects", async () => {
    const feed = useFactoryWorkflows(() => Promise.reject(new Error("down")));

    await feed.load();

    expect(feed.catalog.value).toBe(null);
    expect(feed.error.value).toBe("down");
  });

  it("stays unloaded under SSR so the column renders deterministically", async () => {
    const fetchFn = vi.fn(okJson(catalog));
    const { createSSRApp, h } = await import("vue");
    const { renderToString } = await import("vue/server-renderer");
    let seen;
    const Probe = {
      setup() {
        seen = useFactoryWorkflows(fetchFn);
        return () => h("div");
      },
    };
    const app = createSSRApp({ render: () => h(Probe) });
    await renderToString(app);

    expect(fetchFn).not.toHaveBeenCalled();
    expect(seen.catalog.value).toBe(null);
  });
});
