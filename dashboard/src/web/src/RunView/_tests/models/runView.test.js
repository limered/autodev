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

  describe('time display: terminal runs show a fixed Completed timestamp', () => {
    // The heartbeat sits minutes before the finish so a wrong derivation
    // (lastHeartbeatAt instead of finishedAt) would produce a different string.
    const finishedAt = new Date(nowMs - 30 * 1000).toISOString()
    const heartbeatAt = new Date(nowMs - 300 * 1000).toISOString()

    it('shows a done run as Completed with an absolute timestamp that does not tick', () => {
      const run = {
        ...baseRun,
        status: 'done',
        finishedAt,
        lastHeartbeatAt: heartbeatAt
      }

      const first = runView(run, nowMs)
      const later = runView(run, nowMs + 60 * 1000) // a minute of clock ticks

      expect(first.completed).toBe(new Date(finishedAt).toLocaleString())
      expect(later.completed).toBe(first.completed)
      expect(first.lastSeen).toBeNull()
      expect(first.timeLabel).toBe('Completed')
    })

    it('shows a failed run as Completed with an absolute timestamp that does not tick', () => {
      const run = {
        ...baseRun,
        status: 'failed',
        finishedAt,
        lastHeartbeatAt: heartbeatAt
      }

      const first = runView(run, nowMs)
      const later = runView(run, nowMs + 60 * 1000)

      expect(first.completed).toBe(new Date(finishedAt).toLocaleString())
      expect(later.completed).toBe(first.completed)
      expect(first.lastSeen).toBeNull()
      expect(first.timeLabel).toBe('Completed')
    })

    it('falls back to the last heartbeat when a terminal run has no finishedAt', () => {
      const run = {
        ...baseRun,
        status: 'done',
        finishedAt: null,
        lastHeartbeatAt: heartbeatAt
      }

      const view = runView(run, nowMs)

      expect(view.completed).toBe(new Date(heartbeatAt).toLocaleString())
      expect(view.timeLabel).toBe('Completed')
    })

    it('keeps the live relative Last seen for active runs, ticking with nowMs', () => {
      const run = {
        ...baseRun,
        status: 'running',
        lastHeartbeatAt: new Date(nowMs - 10 * 1000).toISOString()
      }

      const first = runView(run, nowMs)
      const later = runView(run, nowMs + 5 * 1000)

      expect(first.lastSeen).toBe('10s ago')
      expect(later.lastSeen).toBe('15s ago')
      expect(first.completed).toBeNull()
      expect(first.timeLabel).toBe('Last seen')
    })

    it('keeps freshness settled for terminal runs', () => {
      const doneRun = { ...baseRun, status: 'done', finishedAt, lastHeartbeatAt: heartbeatAt }
      const failedRun = { ...baseRun, status: 'failed', finishedAt, lastHeartbeatAt: heartbeatAt }

      expect(runView(doneRun, nowMs).freshnessClass).toBe('settled')
      expect(runView(failedRun, nowMs).freshnessClass).toBe('settled')
    })
  })

  describe('stages', () => {
    const stages = [
      { agent: 'feature-builder', model: 'm1', status: 'done' },
      { agent: 'test-runner', model: 'm2', status: 'running' },
      { agent: 'pr-author', model: 'm3', status: 'pending' }
    ]

    it('maps each stage status to a design-A color class', () => {
      const run = { ...baseRun, stages }

      const view = runView(run, nowMs)

      expect(view.stages).toHaveLength(3)
      expect(view.stages[0]).toEqual({ agent: 'feature-builder', model: 'm1', statusClass: 'stage-done' })
      expect(view.stages[1]).toEqual({ agent: 'test-runner', model: 'm2', statusClass: 'stage-running' })
      expect(view.stages[2]).toEqual({ agent: 'pr-author', model: 'm3', statusClass: 'stage-pending' })
    })

    it('defaults a stage with no status to pending', () => {
      const run = { ...baseRun, stages: [{ agent: 'feature-builder', model: 'm1' }] }

      const view = runView(run, nowMs)

      expect(view.stages[0].statusClass).toBe('stage-pending')
    })

    it('returns an empty stage list when the run has no stages', () => {
      const view = runView(baseRun, nowMs)

      expect(view.stages).toEqual([])
    })
  })
})
