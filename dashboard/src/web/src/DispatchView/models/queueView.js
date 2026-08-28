export function queueStatus(queueItem) {
  if (!queueItem.runId) return 'queued'
  if (queueItem.runStatus === 'done') return 'done'
  if (queueItem.runStatus === 'failed') return 'failed'
  return 'running'
}

export function queueStatusClass(queueItem) {
  return `status-${queueStatus(queueItem)}`
}
