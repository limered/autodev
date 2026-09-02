import { describe, it, expect } from "vitest";
import { hostView } from "../../models/hostView.js";

describe("hostView", () => {
  const nowMs = new Date("2024-06-15T12:00:00Z").getTime();

  it("shows online for a recently seen host", () => {
    const host = {
      online: true,
      lastSeen: new Date(nowMs - 10 * 1000).toISOString(),
    };

    const view = hostView(host, nowMs);

    expect(view.online).toBe(true);
    expect(view.label).toBe("host online");
    expect(view.freshnessClass).toBe("fresh");
    expect(view.lastSeen).toBe("10s ago");
  });

  it("shows offline with last seen for a stale host", () => {
    const host = {
      online: false,
      lastSeen: new Date(nowMs - 90 * 1000).toISOString(),
    };

    const view = hostView(host, nowMs);

    expect(view.online).toBe(false);
    expect(view.label).toBe("host offline");
    expect(view.freshnessClass).toBe("stale-danger");
    expect(view.lastSeen).toBe("1m 30s ago");
  });

  it("shows offline when the host has never been seen", () => {
    const view = hostView(null, nowMs);

    expect(view.online).toBe(false);
    expect(view.label).toBe("host offline");
    expect(view.freshnessClass).toBe("unknown");
    expect(view.lastSeen).toBe("—");
  });
});
