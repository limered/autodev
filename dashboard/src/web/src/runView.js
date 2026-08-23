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
    if (secs > 120) return 'stale-danger'
    if (secs > 30) return 'stale-warn'
    return 'fresh'
  }

  function formatTime(ts) {
    if (!ts) return '—'
    return new Date(ts).toLocaleString()
  }

  const secs = secondsSince(run.lastHeartbeatAt)

  return {
    lastSeen: lastSeenLabel(secs),
    freshnessClass: freshnessClass(secs),
    statusClass: `status-${run.status}`,
    started: formatTime(run.startedAt)
  }
}
