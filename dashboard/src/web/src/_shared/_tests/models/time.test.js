import { describe, it, expect } from "vitest";
import { secondsSince, lastSeenLabel } from "../../../_shared/models/time.js";

describe("time helpers", () => {
  const nowMs = new Date("2024-06-15T12:00:00Z").getTime();

  it("secondsSince returns null for a missing timestamp", () => {
    expect(secondsSince(null, nowMs)).toBeNull();
  });

  it("secondsSince clamps future timestamps to 0", () => {
    expect(secondsSince(new Date(nowMs + 5000).toISOString(), nowMs)).toBe(0);
  });

  it("secondsSince counts elapsed seconds", () => {
    expect(secondsSince(new Date(nowMs - 90 * 1000).toISOString(), nowMs)).toBe(90);
  });

  it("lastSeenLabel formats across the s/m/h boundaries", () => {
    expect(lastSeenLabel(null)).toBe("—");
    expect(lastSeenLabel(45)).toBe("45s ago");
    expect(lastSeenLabel(90)).toBe("1m 30s ago");
    expect(lastSeenLabel(3661)).toBe("1h 1m ago");
  });
});
