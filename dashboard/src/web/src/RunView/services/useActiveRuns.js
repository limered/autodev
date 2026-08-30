import { computed } from 'vue'
import { usePollingFeed } from '../../_shared/services/usePollingFeed.js'
import { isActiveRun } from '../models/runStatus.js'

// Active-runs feed for the runs page split (issue #52). The active set is tiny
// (at most a handful of runs — a single runner today), so one unpaginated fetch
// of the existing /runs/active endpoint covers it; usePollingFeed refreshes that
// fetch every ~5s. Items are re-filtered to non-terminal runs so this feed's
// "only active runs" contract holds even if the endpoint ever returns more.
//
// onSetChange fires only when the *set* of active run ids changes between
// successful polls — a run started (new id) or finished (id gone). Status moves
// within the active set (launching → running → stalled) leave history
// unaffected, so they raise no event; the first successful load seeds the known
// set silently.
export function useActiveRuns(fetchFn = fetch, options = {}) {
  let synced = false
  let knownIds = new Set()

  function activeIdsFrom(items) {
    return new Set(items.filter(isActiveRun).map(r => r.runId))
  }

  function sameIds(a, b) {
    if (a.size !== b.size) return false
    for (const id of a) {
      if (!b.has(id)) return false
    }
    return true
  }

  const feed = usePollingFeed(() => fetchFn('/runs/active'), {
    interval: options.interval,
    onLoaded(items) {
      const ids = activeIdsFrom(items)
      if (!synced) {
        synced = true
        knownIds = ids
        return
      }
      if (sameIds(ids, knownIds)) return
      knownIds = ids
      options.onSetChange?.()
    }
  })

  const runs = computed(() => feed.items.value.filter(isActiveRun))

  return { runs, error: feed.error, load: feed.load }
}
