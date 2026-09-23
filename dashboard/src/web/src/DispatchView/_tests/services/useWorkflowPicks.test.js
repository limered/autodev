import { describe, it, expect } from "vitest";
import { useWorkflowPicks } from "../../services/useWorkflowPicks.js";

describe("useWorkflowPicks", () => {
  it("resolves to the catalog default with no local pick", () => {
    const picks = useWorkflowPicks();

    expect(picks.activePick("q1", "full")).toBe("full");
  });

  it("returns the picked workflow after a pick", () => {
    const picks = useWorkflowPicks();

    picks.setPick("q1", "quick");

    expect(picks.activePick("q1", "full")).toBe("quick");
  });

  it("keeps picks per row", () => {
    const picks = useWorkflowPicks();

    picks.setPick("q1", "quick");

    expect(picks.activePick("q2", "full")).toBe("full");
  });

  it("lets an unclaimed pick change freely", () => {
    const picks = useWorkflowPicks();
    picks.setPick("q1", "quick");

    picks.setPick("q1", "full");

    expect(picks.activePick("q1", "full")).toBe("full");
  });
});
