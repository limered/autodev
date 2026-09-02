import { describe, it, expect } from "vitest";
import { repoColor, REPO_COLOR_PALETTE } from "../../../_shared/models/repoColor.js";

describe("repoColor", () => {
  it("returns the same color for the same repo on every call", () => {
    const first = repoColor("limered/autodev");
    expect(repoColor("limered/autodev")).toBe(first);
    expect(repoColor("limered/autodev")).toBe(first);
  });

  it("returns a valid color value from the fixed palette", () => {
    expect(repoColor("limered/autodev")).toMatch(/^var\(--repo-[a-z]+\)$/);
    for (const repo of ["vuejs/core", "facebook/react", "microsoft/vscode", "dotnet/runtime"]) {
      expect(REPO_COLOR_PALETTE).toContain(repoColor(repo));
    }
  });

  it("spreads different repos across the palette", () => {
    const repos = Array.from({ length: 36 }, (_, i) => `org-${i}/repo-${i}`);
    const distinctColors = new Set(repos.map(repoColor));
    expect(distinctColors.size).toBeGreaterThanOrEqual(4);
  });

  it("falls back to the muted text color when the repo is missing", () => {
    expect(repoColor(null)).toBe("var(--text-muted)");
    expect(repoColor(undefined)).toBe("var(--text-muted)");
    expect(repoColor("")).toBe("var(--text-muted)");
  });
});
