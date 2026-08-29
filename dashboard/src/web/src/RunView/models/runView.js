export function runView(run, nowMs) {
  function secondsSince(ts) {
    if (!ts) return null
    return Math.max(0, Math.round((nowMs - new Date(ts).getTime()) / 1000))
  }

  function lastSeenLabel(secs) {
    if (secs === null) return '—'
    if (secs < 60) return `${secs}s ago`
    if (secs < 3600) return `${Math.floor(secs / 60)}m ${secs % 60}s ago`
    return `${Math.floor(secs / 3600)}h ${Math.floor((secs % 3600) / 60)}m ago`
  }

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

  const secs = secondsSince(run.lastHeartbeatAt)
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
