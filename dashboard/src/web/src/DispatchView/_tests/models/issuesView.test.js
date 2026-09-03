import { describe, it, expect } from "vitest";
import { eligibleIssues } from "../../models/issuesView.js";

// Table-tested with no DOM and no fetch: this is the pure half of the
// eligible-issues column's filter — EligibleIssuesColumn calls it from a
// computed over the two feeds. Each row is one feed state and the issues
// that survive it.
describe("eligibleIssues", () => {
  const issue = (gitHubId, number) => ({
    gitHubId,
    number,
    title: `Fix thing ${number}`,
  });

  it.each([
    [
      "excludes the issues already in the run queue",
      [issue(101, 12), issue(102, 13)],
      new Set([101]),
      [issue(102, 13)],
    ],
    [
      "keeps every issue when nothing is queued",
      [issue(101, 12), issue(102, 13)],
      new Set(),
      [issue(101, 12), issue(102, 13)],
    ],
    [
      "keeps the feed order of the surviving issues",
      [issue(101, 12), issue(102, 13), issue(103, 14)],
      new Set([102]),
      [issue(101, 12), issue(103, 14)],
    ],
    ["returns nothing when every issue is queued", [issue(101, 12)], new Set([101]), []],
    ["returns nothing when the feed has no issues", [], new Set([101]), []],
  ])("%s", (_name, issues, queuedIssueIds, expected) => {
    expect(eligibleIssues(issues, queuedIssueIds)).toEqual(expected);
  });
});
