import { describe, it, expect } from "vitest";
import { isActiveRun, isTerminalRun } from "../../models/runStatus.js";

const run = (status) => ({ runId: `run-${status}`, status });

describe("runStatus", () => {
  it("classifies launching, running and stalled as active (non-terminal)", () => {
    for (const status of ["launching", "running", "stalled"]) {
      expect(isActiveRun(run(status))).toBe(true);
      expect(isTerminalRun(run(status))).toBe(false);
    }
  });

  it("classifies done and failed as terminal (history)", () => {
    for (const status of ["done", "failed"]) {
      expect(isTerminalRun(run(status))).toBe(true);
      expect(isActiveRun(run(status))).toBe(false);
    }
  });

  it("leaves an unknown status in neither list", () => {
    expect(isActiveRun(run("cancelled"))).toBe(false);
    expect(isTerminalRun(run("cancelled"))).toBe(false);
  });
});
