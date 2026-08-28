import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { useQueueFeed } from '../../services/useQueueFeed.js'

describe('useQueueFeed', () => {
  beforeEach(() => {
    vi.useFakeTimers()
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('loads queue items from the fake fetch adapter on start', async () => {
    const item = { id: 1, issueId: 10, rank: 1 }
    const fetchFn = vi.fn().mockResolvedValue({ ok: true, json: async () => [item] })

    const feed = useQueueFeed(fetchFn, { interval: 5000 })
    feed.start()
    await vi.advanceTimersByTimeAsync(0)

    expect(feed.isLoading.value).toBe(false)
    expect(feed.error.value).toBeNull()
    expect(feed.queue.value).toEqual([item])
  })

  it('polls the fetch adapter on the interval', async () => {
    const fetchFn = vi.fn().mockResolvedValue({ ok: true, json: async () => [] })
    const feed = useQueueFeed(fetchFn, { interval: 5000 })

    feed.start()
    await vi.advanceTimersByTimeAsync(0)
    expect(fetchFn).toHaveBeenCalledTimes(1)

    fetchFn.mockResolvedValue({ ok: true, json: async () => [{ id: 2 }] })
    await vi.advanceTimersByTimeAsync(5000)
    expect(fetchFn).toHaveBeenCalledTimes(2)
    expect(feed.queue.value).toEqual([{ id: 2 }])
  })

  it('exposes an error when the fetch adapter fails', async () => {
    const fetchFn = vi.fn().mockRejectedValue(new Error('network down'))
    const feed = useQueueFeed(fetchFn, { interval: 5000 })

    feed.start()
    await vi.advanceTimersByTimeAsync(0)

    expect(feed.error.value).toBe('network down')
    expect(feed.queue.value).toEqual([])
  })
})
