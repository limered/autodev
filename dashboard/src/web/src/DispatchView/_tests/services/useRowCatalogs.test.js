import { describe, it, expect, vi } from "vitest";
import { useRowCatalogs } from "../../services/useRowCatalogs.js";

describe("useRowCatalogs", () => {
  it("fetches each distinct repo once and keys entries by repo", async () => {
    const fetchFn = vi.fn((url) =>
      Promise.resolve({
        ok: true,
        status: 200,
        json: () =>
          Promise.resolve({ repo: url.split("/").pop(), source: "target", content: "{}" }),
      }),
    );
    const catalogs = useRowCatalogs(fetchFn);

    await catalogs.load(["owner/a", "owner/b", "owner/a"]);

    expect(fetchFn).toHaveBeenCalledTimes(2);
    expect(fetchFn).toHaveBeenCalledWith("/catalogs/owner/a");
    expect(fetchFn).toHaveBeenCalledWith("/catalogs/owner/b");
    expect(catalogs.byRepo.value["owner/a"].repo).toBe("a");
    expect(catalogs.byRepo.value["owner/b"].repo).toBe("b");
  });

  it("a failed fetch stores null so the row falls back to the factory feed", async () => {
    const fetchFn = vi.fn(() => Promise.resolve({ ok: false, status: 500 }));
    const catalogs = useRowCatalogs(fetchFn);

    await catalogs.load(["owner/a"]);

    expect(catalogs.byRepo.value["owner/a"]).toBeNull();
  });

  it("a repo already fetched is not re-fetched", async () => {
    const fetchFn = vi.fn(() =>
      Promise.resolve({ ok: true, status: 200, json: () => Promise.resolve({}) }),
    );
    const catalogs = useRowCatalogs(fetchFn);

    await catalogs.load(["owner/a"]);
    await catalogs.load(["owner/a", "owner/b"]);

    expect(fetchFn).toHaveBeenCalledTimes(2);
  });
});
