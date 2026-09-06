// The runId/runStatus → status branch, written once: queueRowView derives
// every per-row concern from the single status it infers.
function queueStatus(queueItem) {
  if (!queueItem.runId) return "queued";
  if (queueItem.runStatus === null || queueItem.runStatus === undefined) return "starting";
  if (queueItem.runStatus === "done") return "done";
  if (queueItem.runStatus === "failed") return "failed";
  return "running";
}

// Row view-model for one queue item: the badge text and the running/failed
// gating the RunQueueColumn template needs, all derived from one status
// inference so the runId/runStatus branching lives in exactly one place
// (replacing the former queueStatus/queueStatusClass/
// isQueueItemRunning/isQueueItemFailed quartet). CSS naming stays in the
// template (`status-${status}`), so this model never speaks class names.
export function queueRowView(queueItem) {
  const status = queueStatus(queueItem);
  return {
    status,
    isRunning: status === "starting" || status === "running",
    isFailed: status === "failed",
  };
}
