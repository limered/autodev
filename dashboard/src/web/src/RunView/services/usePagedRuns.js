import { ref, onMounted, onUnmounted, getCurrentInstance } from 'vue'

// RunView-local paged loader for the main all-runs list. The shared usePollingFeed
// is left untouched (other views depend on its "fetch everything, replace in place"
// contract); instead this owns the infinite-scroll + live-page-1 trade-off:
//
//   - first load fetches page 1 (pageSize runs) and replaces the list;
//   - loadNext() appends the next window, offset by how many rows are already loaded;
//   - a page shorter than pageSize marks the end (hasMore = false) and stops further
//     loading — no spinner, no "no more jobs" message;
//   - refreshFirstPage() re-fetches only the first `pageSize` slots and splices them
//     in place, so in-flight job statuses keep moving while already-scrolled older
//     pages stay put (a top insert may duplicate one row lower down — accepted, same
//     trade-off as offset paging).
//
// The caller injects fetch (mirroring usePollingFeed/useQueueActions) so the paging
// logic is testable through this interface with a fake fetchFn and no timers.
export function usePagedRuns(fetchFn = fetch, options = {}) {
  const pageSize = options.pageSize ?? 10
  const interval = options.interval ?? 5000

  const runs = ref([])
  const error = ref(null)
  const isLoading = ref(false)
  const hasMore = ref(true)

  let pollTimer = null

  async function fetchPage(skip, take) {
    const res = await fetchFn(`/runs?skip=${skip}&take=${take}`)
    if (!res.ok) throw new Error(`HTTP ${res.status}`)
    return await res.json()
  }

  // Initial load: replace the whole list with page 1. A short first page means there
  // are no more runs to scroll into, so the end is reached immediately.
  async function loadFirst() {
    isLoading.value = true
    try {
      const page = await fetchPage(0, pageSize)
      runs.value = page
      error.value = null
      if (page.length < pageSize) hasMore.value = false
    } catch (e) {
      error.value = e.message
    } finally {
      isLoading.value = false
    }
  }

  // Scroll-triggered: append the next window after whatever is already loaded. No-ops
  // once the end is reached or while a page is already in flight (re-entrant guard).
  async function loadNext() {
    if (!hasMore.value || isLoading.value) return
    isLoading.value = true
    try {
      const page = await fetchPage(runs.value.length, pageSize)
      runs.value = [...runs.value, ...page]
      error.value = null
      if (page.length < pageSize) hasMore.value = false
    } catch (e) {
      error.value = e.message
    } finally {
      isLoading.value = false
    }
  }

  // Poll-triggered: refresh only the first `pageSize` slice in place, leaving any
  // appended tail exactly where it is so a live update never yanks scroll position.
  async function refreshFirstPage() {
    try {
      const page = await fetchPage(0, pageSize)
      runs.value = [...page, ...runs.value.slice(pageSize)]
      error.value = null
    } catch (e) {
      error.value = e.message
    }
  }

  function start() {
    stop()
    loadFirst()
    pollTimer = setInterval(refreshFirstPage, interval)
  }

  function stop() {
    if (pollTimer !== null) {
      clearInterval(pollTimer)
      pollTimer = null
    }
  }

  if (getCurrentInstance()) {
    onMounted(start)
    onUnmounted(stop)
  }

  return { runs, error, isLoading, hasMore, loadFirst, loadNext, refreshFirstPage, start, stop }
}
