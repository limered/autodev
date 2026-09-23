import { describe, it, expect } from "vitest";
import { useWorkflowPicks } from "../../services/useWorkflowPicks.js";

// Local per-row picks for the picker shell: the active pick defaults to the
// catalog default and is freely changeable until the row is claimed. No
// backend write in this shell — the claim-freeze pass persists the pick.
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
