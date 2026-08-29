import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { useHostFeed } from '../../services/useHostFeed.js'

describe('useHostFeed', () => {
  beforeEach(() => {
    vi.useFakeTimers()
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('loads host state from the fake fetch adapter on start', async () => {
    const host = { online: true, lastSeen: '2024-06-15T12:00:00Z' }
    const fetchFn = vi.fn().mockResolvedValue({ ok: true, json: async () => host })

    const feed = useHostFeed(fetchFn, { interval: 5000 })
    feed.start()
    await vi.advanceTimersByTimeAsync(0)

    expect(feed.isLoading.value).toBe(false)
    expect(feed.error.value).toBeNull()
    expect(feed.host.value).toEqual(host)
  })

  it('polls the fetch adapter on the interval', async () => {
    const fetchFn = vi.fn().mockResolvedValue({ ok: true, json: async () => ({ online: true }) })
    const feed = useHostFeed(fetchFn, { interval: 5000 })

    feed.start()
    await vi.advanceTimersByTimeAsync(0)
    expect(fetchFn).toHaveBeenCalledTimes(1)

    fetchFn.mockResolvedValue({ ok: true, json: async () => ({ online: false }) })
    await vi.advanceTimersByTimeAsync(5000)
    expect(fetchFn).toHaveBeenCalledTimes(2)
    expect(feed.host.value).toEqual({ online: false })
  })

  it('exposes an error when the fetch adapter fails', async () => {
    const fetchFn = vi.fn().mockRejectedValue(new Error('network down'))
    const feed = useHostFeed(fetchFn, { interval: 5000 })

    feed.start()
    await vi.advanceTimersByTimeAsync(0)

    expect(feed.isLoading.value).toBe(false)
    expect(feed.error.value).toBe('network down')
  })
})
