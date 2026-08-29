import { describe, it, expect, vi } from 'vitest'
import { usePagedRuns } from '../../services/usePagedRuns.js'

function pageResponse(items) {
  return { ok: true, status: 200, json: () => Promise.resolve(items) }
}

// Deterministic run fixtures. id `run-k` is started k seconds after the epoch so the
// started_at DESC order the API promises maps to descending ids.
function makeRuns(n, startId = 0) {
  return Array.from({ length: n }, (_, i) => ({
    runId: `run-${startId + i}`,
    repo: 'owner/repo',
    branch: 'main',
    status: 'done',
    startedAt: new Date((startId + i) * 1000).toISOString()
  }))
}

describe('usePagedRuns', () => {
  it('first load yields the first page of 10 runs and stays hungry for more', async () => {
    const fetchFn = vi.fn(() => Promise.resolve(pageResponse(makeRuns(10))))
    const feed = usePagedRuns(fetchFn)

    await feed.loadFirst()

    expect(fetchFn).toHaveBeenCalledWith('/runs?skip=0&take=10')
    expect(feed.runs.value).toHaveLength(10)
    expect(feed.hasMore.value).toBe(true)
    expect(feed.error.value).toBeNull()
  })

  it('load next appends the next window offset by the rows already loaded', async () => {
    let call = 0
    const pages = [makeRuns(10, 0), makeRuns(10, 10)]
    const fetchFn = vi.fn(() => Promise.resolve(pageResponse(pages[call++])))
    const feed = usePagedRuns(fetchFn)

    await feed.loadFirst()
    await feed.loadNext()

    expect(fetchFn).toHaveBeenNthCalledWith(2, '/runs?skip=10&take=10')
    expect(feed.runs.value).toHaveLength(20)
    expect(feed.runs.value[10].runId).toBe('run-10')
    expect(feed.hasMore.value).toBe(true)
  })

  it('a short page marks the end and stops further loading', async () => {
    let call = 0
    const pages = [makeRuns(10, 0), makeRuns(4, 10)] // short second page
    const fetchFn = vi.fn(() => Promise.resolve(pageResponse(pages[call++])))
    const feed = usePagedRuns(fetchFn)

    await feed.loadFirst()
    await feed.loadNext()

    expect(feed.runs.value).toHaveLength(14)
    expect(feed.hasMore.value).toBe(false)

    await feed.loadNext() // end reached — must no-op
    expect(fetchFn).toHaveBeenCalledTimes(2)
    expect(feed.runs.value).toHaveLength(14)
  })

  it('marks the end immediately when the first page is short', async () => {
    const fetchFn = vi.fn(() => Promise.resolve(pageResponse(makeRuns(3))))
    const feed = usePagedRuns(fetchFn)

    await feed.loadFirst()

    expect(feed.runs.value).toHaveLength(3)
    expect(feed.hasMore.value).toBe(false)
  })

  it('page-1 refresh replaces only the first slice without dropping appended pages', async () => {
    let call = 0
    const refreshedFirstPage = makeRuns(10, 0).map(r => ({ ...r, status: 'running' }))
    const pages = [makeRuns(10, 0), makeRuns(10, 10), refreshedFirstPage]
    const fetchFn = vi.fn(() => Promise.resolve(pageResponse(pages[call++])))
    const feed = usePagedRuns(fetchFn)

    await feed.loadFirst()
    await feed.loadNext()
    expect(feed.runs.value).toHaveLength(20)

    await feed.refreshFirstPage()

    expect(fetchFn).toHaveBeenLastCalledWith('/runs?skip=0&take=10')
    expect(feed.runs.value).toHaveLength(20) // appended page still there
    expect(feed.runs.value[0].status).toBe('running') // first slice refreshed
    expect(feed.runs.value[10].runId).toBe('run-10') // appended tail preserved
  })

  it('ignores re-entrant load-next calls while a page is in flight', async () => {
    const firstPage = pageResponse(makeRuns(10, 0))
    let resolveNext
    const fetchFn = vi.fn(url => {
      if (url === '/runs?skip=0&take=10') return Promise.resolve(firstPage)
      return new Promise(r => { resolveNext = () => r(pageResponse(makeRuns(10, 10))) })
    })
    const feed = usePagedRuns(fetchFn)
    await feed.loadFirst()

    const first = feed.loadNext() // in flight
    await feed.loadNext() // re-entrant — must no-op
    expect(fetchFn).toHaveBeenCalledTimes(2) // loadFirst + one loadNext only
    resolveNext()
    await first
  })

  it('records an error on a failed fetch without dropping already-loaded runs', async () => {
    let call = 0
    const fetchFn = vi.fn(() => {
      call++
      if (call === 2) return Promise.resolve({ ok: false, status: 500 })
      return Promise.resolve(pageResponse(makeRuns(10)))
    })
    const feed = usePagedRuns(fetchFn)

    await feed.loadFirst()
    await feed.loadNext() // 500

    expect(feed.error.value).toBe('HTTP 500')
    expect(feed.runs.value).toHaveLength(10) // first page preserved
    expect(feed.hasMore.value).toBe(true) // end not reached; scroll can retry
  })
})
