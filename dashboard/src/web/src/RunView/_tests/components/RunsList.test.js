import { describe, it, expect, vi } from 'vitest'
import { createSSRApp, h } from 'vue'
import { renderToString } from 'vue/server-renderer'
import RunsList from '../../components/RunsList.vue'
import { useActiveRuns } from '../../services/useActiveRuns.js'
import { usePagedRuns } from '../../services/usePagedRuns.js'

function ok(items) {
  return { ok: true, status: 200, json: () => Promise.resolve(items) }
}

// Deterministic history fixtures: id `run-k` is started k seconds after the
// epoch so the started_at DESC order the API promises maps to descending ids.
function makeRuns(n, startId = 0) {
  return Array.from({ length: n }, (_, i) => ({
    runId: `run-${startId + i}`,
    repo: 'owner/repo',
    branch: 'main',
    status: 'done',
    startedAt: new Date((startId + i) * 1000).toISOString()
  }))
}

const run = (id, status) => ({
  runId: id,
  repo: 'owner/repo',
  branch: 'main',
  status,
  startedAt: '2026-08-30T00:00:00Z'
})

// RunsList.vue wires the split as: ActiveRunsList's set-change event →
// RunsHistoryList.refreshFirst(). The components fetch on mount, which needs a
// DOM lifecycle this harness doesn't have, so the container's contract is
// exercised here at the composable seam with exactly that wiring.
// The container's refresh handler is fire-and-forget (the UI never awaits it),
// so tests flush one macrotask to let the triggered refresh land.
const flush = () => new Promise(resolve => setTimeout(resolve, 0))

function makeWiredLists({ activeFetch, historyFetch }) {
  const history = usePagedRuns(historyFetch)
  const active = useActiveRuns(activeFetch, {
    onSetChange: () => history.refreshFirst()
  })
  return { active, history }
}

describe('RunsList container wiring', () => {
  it('active polling with a steady set never touches history pagination', async () => {
    const activeFetch = vi.fn(() => Promise.resolve(ok([run('a', 'running')])))
    const historyFetch = vi.fn(() => Promise.resolve(ok(makeRuns(10))))
    const { active, history } = makeWiredLists({ activeFetch, historyFetch })

    await history.loadFirst()
    await active.load() // seed the active set
    await active.load() // poll — same set
    await active.load() // poll — same set

    expect(historyFetch).toHaveBeenCalledTimes(1) // only the initial first page
    expect(historyFetch).toHaveBeenCalledWith('/runs?skip=0&take=10')
  })

  it('refreshes history first page once when a run finishes, keeping later pages', async () => {
    let activeItems = [run('a', 'running')]
    const activeFetch = vi.fn(() => Promise.resolve(ok(activeItems)))
    let call = 0
    const pages = [makeRuns(10, 0), makeRuns(10, 10), makeRuns(10, 100)]
    const historyFetch = vi.fn(() => Promise.resolve(ok(pages[call++])))
    const { active, history } = makeWiredLists({ activeFetch, historyFetch })

    await history.loadFirst()
    await history.loadNext() // two pages loaded
    await active.load() // seed: a is active

    activeItems = [] // a finished → it should now appear at the top of history
    await active.load()
    await flush()

    expect(historyFetch).toHaveBeenCalledTimes(3) // loadFirst + loadNext + one refresh
    expect(historyFetch).toHaveBeenLastCalledWith('/runs?skip=0&take=10')
    expect(history.runs.value.slice(0, 10)).toEqual(makeRuns(10, 100)) // fresh first page
    expect(history.runs.value.slice(10)).toEqual(makeRuns(10, 10)) // appended page kept
  })

  it('refreshes history first page once when a run starts', async () => {
    let activeItems = []
    const activeFetch = vi.fn(() => Promise.resolve(ok(activeItems)))
    let call = 0
    const pages = [makeRuns(10, 0), makeRuns(10, 0)]
    const historyFetch = vi.fn(() => Promise.resolve(ok(pages[call++])))
    const { active, history } = makeWiredLists({ activeFetch, historyFetch })

    await history.loadFirst()
    await active.load() // seed: idle

    activeItems = [run('b', 'launching')]
    await active.load() // b started

    expect(historyFetch).toHaveBeenCalledTimes(2) // loadFirst + one refresh
    expect(historyFetch).toHaveBeenLastCalledWith('/runs?skip=0&take=10')
  })
})

describe('RunsList container markup', () => {
  // Rendered via SSR (mirroring ErrorBanner's test) so the container shape can
  // be asserted without a DOM: history always renders with its empty state,
  // while the active section stays hidden while nothing is active.
  it('renders history with its empty state and hides the active section while idle', async () => {
    const html = await renderToString(createSSRApp({ render: () => h(RunsList) }))

    expect(html).toContain('aria-label="Run history"')
    expect(html).toContain('No finished runs yet')
    expect(html).not.toContain('aria-label="Active runs"')
  })
})
