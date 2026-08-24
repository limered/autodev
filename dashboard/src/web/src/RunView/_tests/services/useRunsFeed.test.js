import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { useRunsFeed } from '../../services/useRunsFeed.js'

describe('useRunsFeed', () => {
  beforeEach(() => {
    vi.useFakeTimers()
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('loads runs from the fake fetch adapter on start', async () => {
    const run = { runId: 'run-1', repo: 'a/b', branch: 'main', status: 'running' }
    const fetchFn = vi.fn().mockResolvedValue({ ok: true, json: async () => [run] })

    const feed = useRunsFeed(fetchFn, { interval: 5000 })
    feed.start()
    await vi.advanceTimersByTimeAsync(0)

    expect(feed.isLoading.value).toBe(false)
    expect(feed.error.value).toBeNull()
    expect(feed.runs.value).toEqual([run])
  })

  it('polls the fetch adapter on the interval', async () => {
    const fetchFn = vi.fn().mockResolvedValue({ ok: true, json: async () => [] })
    const feed = useRunsFeed(fetchFn, { interval: 5000 })

    feed.start()
    await vi.advanceTimersByTimeAsync(0)
    expect(fetchFn).toHaveBeenCalledTimes(1)

    fetchFn.mockResolvedValue({ ok: true, json: async () => [{ runId: 'run-2' }] })
    await vi.advanceTimersByTimeAsync(5000)
    expect(fetchFn).toHaveBeenCalledTimes(2)
    expect(feed.runs.value).toEqual([{ runId: 'run-2' }])
  })

  it('exposes an error when the fetch adapter fails', async () => {
    const fetchFn = vi.fn().mockRejectedValue(new Error('network down'))
    const feed = useRunsFeed(fetchFn, { interval: 5000 })

    feed.start()
    await vi.advanceTimersByTimeAsync(0)

    expect(feed.isLoading.value).toBe(false)
    expect(feed.error.value).toBe('network down')
    expect(feed.runs.value).toEqual([])
  })

  it('exposes an error for non-ok responses', async () => {
    const fetchFn = vi.fn().mockResolvedValue({ ok: false, status: 503, json: async () => [] })
    const feed = useRunsFeed(fetchFn, { interval: 5000 })

    feed.start()
    await vi.advanceTimersByTimeAsync(0)

    expect(feed.error.value).toBe('HTTP 503')
  })
})
