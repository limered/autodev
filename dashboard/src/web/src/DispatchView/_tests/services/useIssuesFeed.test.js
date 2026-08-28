import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { useIssuesFeed } from '../../services/useIssuesFeed.js'

describe('useIssuesFeed', () => {
  beforeEach(() => {
    vi.useFakeTimers()
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('loads issues from the fake fetch adapter on start', async () => {
    const issue = { githubId: 1, repo: 'a/b', number: 2, title: 'An issue' }
    const fetchFn = vi.fn().mockResolvedValue({ ok: true, json: async () => [issue] })

    const feed = useIssuesFeed(fetchFn, { interval: 5000 })
    feed.start()
    await vi.advanceTimersByTimeAsync(0)

    expect(feed.isLoading.value).toBe(false)
    expect(feed.error.value).toBeNull()
    expect(feed.issues.value).toEqual([issue])
  })

  it('polls the fetch adapter on the interval', async () => {
    const fetchFn = vi.fn().mockResolvedValue({ ok: true, json: async () => [] })
    const feed = useIssuesFeed(fetchFn, { interval: 5000 })

    feed.start()
    await vi.advanceTimersByTimeAsync(0)
    expect(fetchFn).toHaveBeenCalledTimes(1)

    fetchFn.mockResolvedValue({ ok: true, json: async () => [{ githubId: 2 }] })
    await vi.advanceTimersByTimeAsync(5000)
    expect(fetchFn).toHaveBeenCalledTimes(2)
    expect(feed.issues.value).toEqual([{ githubId: 2 }])
  })

  it('exposes an error when the fetch adapter fails', async () => {
    const fetchFn = vi.fn().mockRejectedValue(new Error('network down'))
    const feed = useIssuesFeed(fetchFn, { interval: 5000 })

    feed.start()
    await vi.advanceTimersByTimeAsync(0)

    expect(feed.isLoading.value).toBe(false)
    expect(feed.error.value).toBe('network down')
    expect(feed.issues.value).toEqual([])
  })

  it('exposes an error for non-ok responses', async () => {
    const fetchFn = vi.fn().mockResolvedValue({ ok: false, status: 503, json: async () => [] })
    const feed = useIssuesFeed(fetchFn, { interval: 5000 })

    feed.start()
    await vi.advanceTimersByTimeAsync(0)

    expect(feed.error.value).toBe('HTTP 503')
  })
})
