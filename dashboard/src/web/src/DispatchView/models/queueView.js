export function queueStatus(queueItem) {
  if (!queueItem.runId) return "queued";
  if (queueItem.runStatus === null || queueItem.runStatus === undefined) return "starting";
  if (queueItem.runStatus === "done") return "done";
  if (queueItem.runStatus === "failed") return "failed";
  return "running";
}

export function queueStatusClass(queueItem) {
  return `status-${queueStatus(queueItem)}`;
}

export function isQueueItemRunning(queueItem) {
  return Boolean(
    queueItem.runId && queueItem.runStatus !== "done" && queueItem.runStatus !== "failed",
  );
}

export function isQueueItemFailed(queueItem) {
  return queueStatus(queueItem) === "failed";
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
