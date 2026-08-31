import { secondsSince, lastSeenLabel } from '../../_shared/models/time.js'
import { isTerminalRun } from './runStatus.js'

export function runView(run, nowMs) {
  // Terminal runs are no longer "seen": instead of a live relative label that
  // ticks upward forever, they show a fixed Completed timestamp derived from
  // the run's own stored times (finishedAt, falling back to lastHeartbeatAt),
  // so the value is stable across the 1s clock tick. Active runs keep the
  // live "Last seen" behaviour.
  const terminal = isTerminalRun(run)

  function freshnessClass(secs) {
    if (secs === null) return 'unknown'
    if (terminal) return 'settled'
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
    timeLabel: terminal ? 'Completed' : 'Last seen',
    lastSeen: terminal ? null : lastSeenLabel(secs),
    completed: terminal ? formatTime(run.finishedAt ?? run.lastHeartbeatAt) : null,
    freshnessClass: freshnessClass(secs),
    statusClass: `status-${run.status}`,
    started: formatTime(run.startedAt),
    stages
  }
}
