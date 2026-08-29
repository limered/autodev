export function secondsSince(ts, nowMs) {
  if (!ts) return null
  return Math.max(0, Math.round((nowMs - new Date(ts).getTime()) / 1000))
}

export function lastSeenLabel(secs) {
  if (secs === null) return '—'
  if (secs < 60) return `${secs}s ago`
  if (secs < 3600) return `${Math.floor(secs / 60)}m ${secs % 60}s ago`
  return `${Math.floor(secs / 3600)}h ${Math.floor((secs % 3600) / 60)}m ago`
}
