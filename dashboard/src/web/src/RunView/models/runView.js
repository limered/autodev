import { secondsSince, lastSeenLabel } from '../../_shared/models/time.js'

export function runView(run, nowMs) {
  function freshnessClass(secs) {
    if (secs === null) return 'unknown'
    if (run.status === 'failed' || run.status === 'done') return 'settled'
    if (run.status === 'stalled') return 'stale-warn'
    if (secs > 120) return 'stale-danger'
    if (secs > 30) return 'stale-warn'
    return 'fresh'
  }

  function formatTime(ts) {
    if (!ts) return '—'
    return new Date(ts).toLocaleString()
  }

  // Design-A badge strip: pending grey, running green, done blue. The backend
  // derives each stage's status from the seeded stages plus currentPhase.
  function stageStatusClass(status) {
    if (status === 'done') return 'stage-done'
    if (status === 'running') return 'stage-running'
    return 'stage-pending'
  }

  const secs = secondsSince(run.lastHeartbeatAt, nowMs)
  const stages = (run.stages || []).map(s => ({
    agent: s.agent,
    model: s.model,
    statusClass: stageStatusClass(s.status)
  }))

  return {
    lastSeen: lastSeenLabel(secs),
    freshnessClass: freshnessClass(secs),
    statusClass: `status-${run.status}`,
    started: formatTime(run.startedAt),
    stages
  }
}
