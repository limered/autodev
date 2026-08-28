export function queueStatus(queueItem) {
  if (!queueItem.runId) return 'queued'
  if (queueItem.runStatus === null || queueItem.runStatus === undefined) return 'starting'
  if (queueItem.runStatus === 'done') return 'done'
  if (queueItem.runStatus === 'failed') return 'failed'
  return 'running'
}

export function queueStatusClass(queueItem) {
  return `status-${queueStatus(queueItem)}`
}

export function isQueueItemRunning(queueItem) {
  return Boolean(queueItem.runId && queueItem.runStatus !== 'done' && queueItem.runStatus !== 'failed')
}
