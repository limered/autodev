// The runId/runStatus → status branch, written once: queueRowView derives
// every per-row concern from the single status it infers.
function queueStatus(queueItem) {
  if (!queueItem.runId) return "queued";
  if (queueItem.runStatus === null || queueItem.runStatus === undefined) return "starting";
  if (queueItem.runStatus === "done") return "done";
  if (queueItem.runStatus === "failed") return "failed";
  return "running";
}

// Row view-model for one queue item: the badge text, its status-* class, and
// the running/failed gating the RunQueueColumn template needs, all derived
// from one status inference so the runId/runStatus branching lives in exactly
// one place (replacing the former queueStatus/queueStatusClass/
// isQueueItemRunning/isQueueItemFailed quartet).
export function queueRowView(queueItem) {
  const status = queueStatus(queueItem);
  return {
    status,
    statusClass: `status-${status}`,
    isRunning: status === "starting" || status === "running",
    isFailed: status === "failed",
  };
}

// Convergence policy for the queue column's localQueue shadow copy (issue
// #75): while the column is idle the feed is the truth — take it, copied so
// the shadow never aliases the feed array — but while a save is in flight or
// a drag is active the local order wins, so a polling sync never yanks rows
// out from under an in-progress interaction. Pure on purpose: the ref/watch
// plumbing in RunQueueColumn calls it, and the interleaving is table-tested
// with no DOM and no fake fetch.
export function resolveLocalQueue(feedQueue, localQueue, { isSaving, isDragging } = {}) {
  if (isSaving || isDragging) return localQueue;
  return [...feedQueue];
}
