import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { useNowTicker } from "../../services/useNowTicker.js";

describe("useNowTicker", () => {
  beforeEach(() => {
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it("advances now on the interval", () => {
    const ticker = useNowTicker(1000);
    const first = ticker.now.value;

    ticker.start();
    vi.advanceTimersByTime(3000);

    expect(ticker.now.value - first).toBe(3000);
    ticker.stop();
  });

  it("stop halts the ticker", () => {
    const ticker = useNowTicker(1000);
    ticker.start();
    vi.advanceTimersByTime(1000);
    const atStop = ticker.now.value;

    ticker.stop();
    vi.advanceTimersByTime(5000);

    expect(ticker.now.value).toBe(atStop);
  });

  it("start is idempotent and stop-without-start is safe", () => {
    const ticker = useNowTicker(1000);
    expect(() => ticker.stop()).not.toThrow();

    ticker.start();
    ticker.start();
    vi.advanceTimersByTime(2000);

    expect(ticker.now.value).toBeLessThanOrEqual(Date.now());
    ticker.stop();
  });
});
