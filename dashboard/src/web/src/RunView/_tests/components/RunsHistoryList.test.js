// @vitest-environment happy-dom
import { describe, it, expect, vi } from "vitest";
import { createApp, h, nextTick } from "vue";
import RunsHistoryList from "../../components/RunsHistoryList.vue";

const failed = (overrides = {}) => ({
  runId: "run-old",
  repo: "owner/repo",
  branch: "factory/issue-1-abc",
  spec: "Do the thing",
  status: "failed",
  startedAt: "2026-09-04T11:00:00Z",
  finishedAt: "2026-09-04T11:30:00Z",
  failureReason: "boom",
  ...overrides,
});

function mountList(fetchFn) {
  vi.stubGlobal("fetch", fetchFn);
  const host = document.createElement("div");
  document.body.appendChild(host);
  const app = createApp({ render: () => h(RunsHistoryList) });
  app.mount(host);
  return { host, app };
}

async function flush(times = 4) {
  for (let i = 0; i < times; i++) await nextTick();
  await new Promise((r) => setTimeout(r, 0));
  for (let i = 0; i < times; i++) await nextTick();
}

describe("RunsHistoryList restart", () => {
  it("shows a restart control on failed history cards", async () => {
    const fetchFn = vi.fn((url) => {
      if (String(url).startsWith("/runs?")) {
        return Promise.resolve({ ok: true, json: () => Promise.resolve([failed()]) });
      }
      return Promise.reject(new Error(`unexpected ${url}`));
    });
    const { host, app } = mountList(fetchFn);
    await flush();

    expect(host.querySelector(".restart-run")).not.toBeNull();

    app.unmount();
    host.remove();
    vi.unstubAllGlobals();
  });

  it("posts a restart and refreshes without a manual command when queue-bound", async () => {
    const calls = [];
    const fetchFn = vi.fn((url, opts) => {
      calls.push([String(url), opts?.method]);
      if (String(url).startsWith("/runs?")) {
        return Promise.resolve({ ok: true, json: () => Promise.resolve([failed()]) });
      }
      if (String(url).endsWith("/restart")) {
        return Promise.resolve({
          ok: true,
          json: () =>
            Promise.resolve({
              repo: "owner/repo",
              branch: "factory/issue-1-abc",
              spec: "Do the thing",
              resumeStage: "review-loop",
              parentRunId: "run-old",
              queueId: 7,
            }),
        });
      }
      return Promise.reject(new Error(`unexpected ${url}`));
    });
    const { host, app } = mountList(fetchFn);
    await flush();

    host.querySelector(".restart-run").click();
    await flush();

    expect(calls).toContainEqual(["/runs/run-old/restart", "POST"]);
    expect(host.querySelector(".resume-command")).toBeNull();

    app.unmount();
    host.remove();
    vi.unstubAllGlobals();
  });

  it("shows the copyable Runner invocation for standalone restarts", async () => {
    const fetchFn = vi.fn((url) => {
      if (String(url).startsWith("/runs?")) {
        return Promise.resolve({ ok: true, json: () => Promise.resolve([failed()]) });
      }
      return Promise.resolve({
        ok: true,
        json: () =>
          Promise.resolve({
            repo: "owner/repo",
            branch: "factory/issue-1-abc",
            spec: "Do the thing",
            resumeStage: "review-loop",
            parentRunId: "run-old",
            queueId: null,
          }),
      });
    });
    const { host, app } = mountList(fetchFn);
    await flush();

    host.querySelector(".restart-run").click();
    await flush();

    const banner = host.querySelector(".resume-command code");
    expect(banner).not.toBeNull();
    expect(banner.textContent).toContain("start-job.ps1");
    expect(banner.textContent).toContain("factory/issue-1-abc");
    expect(banner.textContent).toContain("review-loop");
    expect(banner.textContent).toContain("run-old");

    app.unmount();
    host.remove();
    vi.unstubAllGlobals();
  });

  it("marks the failed card restarted once the retry row loads", async () => {
    const child = {
      ...failed({ runId: "run-new", status: "running", parentRunId: "run-old" }),
      lastHeartbeatAt: new Date().toISOString(),
    };
    const fetchFn = vi.fn((url) => {
      if (String(url).startsWith("/runs?")) {
        return Promise.resolve({ ok: true, json: () => Promise.resolve([child, failed()]) });
      }
      return Promise.reject(new Error(`unexpected ${url}`));
    });
    const { host, app } = mountList(fetchFn);
    await flush();

    expect(host.innerHTML).toContain("restarted-link");
    expect(host.innerHTML).toContain("run-new");

    app.unmount();
    host.remove();
    vi.unstubAllGlobals();
  });
});
