// Status classification for the runs page split (issue #52): the tiny,
// always-changing set (launching/running/stalled) is polled in the active list,
// while the settled set (done/failed) is paginated as history. The backend only
// ever emits these five statuses; anything unknown falls in neither list rather
// than silently landing in history.
const ACTIVE_STATUSES = new Set(['launching', 'running', 'stalled'])
const TERMINAL_STATUSES = new Set(['done', 'failed'])

export function isActiveRun(run) {
  return ACTIVE_STATUSES.has(run.status)
}

export function isTerminalRun(run) {
  return TERMINAL_STATUSES.has(run.status)
}
