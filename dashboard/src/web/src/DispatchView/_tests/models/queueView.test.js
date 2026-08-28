import { describe, it, expect } from 'vitest'
import { queueStatus, queueStatusClass } from '../../models/queueView.js'

describe('queueStatus', () => {
  it('returns queued when no run is linked', () => {
    expect(queueStatus({ runId: null })).toBe('queued')
  })

  it('returns running for launching, running, or stalled runs', () => {
    expect(queueStatus({ runId: 'r1', runStatus: 'launching' })).toBe('running')
    expect(queueStatus({ runId: 'r1', runStatus: 'running' })).toBe('running')
    expect(queueStatus({ runId: 'r1', runStatus: 'stalled' })).toBe('running')
  })

  it('returns done for finished runs', () => {
    expect(queueStatus({ runId: 'r1', runStatus: 'done' })).toBe('done')
  })

  it('returns failed for failed runs', () => {
    expect(queueStatus({ runId: 'r1', runStatus: 'failed' })).toBe('failed')
  })
})

describe('queueStatusClass', () => {
  it('prefixes the inferred status with status-', () => {
    expect(queueStatusClass({ runId: null })).toBe('status-queued')
    expect(queueStatusClass({ runId: 'r1', runStatus: 'done' })).toBe('status-done')
  })
})
