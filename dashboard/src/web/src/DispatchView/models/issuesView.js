// The eligible-issues column's view of the /issues feed: which synced open
// issues count as eligible — everything not already sitting in the run
// queue. Pure on purpose: EligibleIssuesColumn calls it from a computed over
// the two feeds, so the filtering is table-tested with no DOM and no fetch.
export function eligibleIssues(issues, queuedIssueIds) {
  return issues.filter((issue) => !queuedIssueIds.has(issue.gitHubId));
}
