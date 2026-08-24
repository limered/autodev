import { describe, it, expect } from 'vitest'
import { runView } from '../../models/runView.js'

describe('runView', () => {
  const nowMs = new Date('2024-06-15T12:00:00Z').getTime()

  const baseRun = {
    runId: 'run-1',
    repo: 'owner/repo',
    branch: 'main',
    model: 'test-model',
    status: 'running',
    startedAt: new Date(nowMs - 5 * 60 * 1000).toISOString()
  }

  it('returns fresh values for a recently-seen running run', () => {
    const run = {
      ...baseRun,
      lastHeartbeatAt: new Date(nowMs - 10 * 1000).toISOString()
    }

    const view = runView(run, nowMs)

    expect(view.lastSeen).toBe('10s ago')
    expect(view.freshnessClass).toBe('fresh')
    expect(view.statusClass).toBe('status-running')
    expect(view.started).not.toBe('—')
  })

  it('warns when the heartbeat is moderately stale', () => {
    const run = {
      ...baseRun,
      lastHeartbeatAt: new Date(nowMs - 45 * 1000).toISOString()
    }

    expect(runView(run, nowMs).freshnessClass).toBe('stale-warn')
    expect(runView(run, nowMs).lastSeen).toBe('45s ago')
  })

  it('marks very stale running runs as danger', () => {
    const run = {
      ...baseRun,
      lastHeartbeatAt: new Date(nowMs - 125 * 1000).toISOString()
    }

    expect(runView(run, nowMs).freshnessClass).toBe('stale-danger')
    expect(runView(run, nowMs).lastSeen).toBe('2m 5s ago')
  })

  it('treats done and failed runs as settled regardless of heartbeat age', () => {
    const doneRun = {
      ...baseRun,
      status: 'done',
      lastHeartbeatAt: new Date(nowMs - 300 * 1000).toISOString()
    }
    const failedRun = {
      ...baseRun,
      status: 'failed',
      lastHeartbeatAt: new Date(nowMs - 300 * 1000).toISOString()
    }

    expect(runView(doneRun, nowMs).freshnessClass).toBe('settled')
    expect(runView(doneRun, nowMs).statusClass).toBe('status-done')
    expect(runView(failedRun, nowMs).freshnessClass).toBe('settled')
    expect(runView(failedRun, nowMs).statusClass).toBe('status-failed')
  })

  it('honours a stalled status even when the heartbeat is fresh', () => {
    const run = {
      ...baseRun,
      status: 'stalled',
      lastHeartbeatAt: new Date(nowMs - 5 * 1000).toISOString()
    }

    expect(runView(run, nowMs).freshnessClass).toBe('stale-warn')
    expect(runView(run, nowMs).lastSeen).toBe('5s ago')
  })

  it('does not escalate stalled runs to danger based on heartbeat age', () => {
    const run = {
      ...baseRun,
      status: 'stalled',
      lastHeartbeatAt: new Date(nowMs - 300 * 1000).toISOString()
    }

    expect(runView(run, nowMs).freshnessClass).toBe('stale-warn')
  })

  it('handles missing heartbeat gracefully', () => {
    const run = { ...baseRun, lastHeartbeatAt: null }

    const view = runView(run, nowMs)

    expect(view.lastSeen).toBe('—')
    expect(view.freshnessClass).toBe('unknown')
  })

  it('formats long durations in hours and minutes', () => {
    const run = {
      ...baseRun,
      lastHeartbeatAt: new Date(nowMs - 3665 * 1000).toISOString()
    }

    expect(runView(run, nowMs).lastSeen).toBe('1h 1m ago')
  })

  it('returns em-dashes for missing started timestamp', () => {
    const run = { ...baseRun, startedAt: null }

    expect(runView(run, nowMs).started).toBe('—')
  })
})
