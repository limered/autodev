import { getCurrentInstance, onMounted, ref } from 'vue'

// RunView-local paged loader for the main all-runs list. Loads the first 10 runs, then
// appends the next 10 each time loadNext() is called (driven by a "Load more" button).
//
// There is deliberately NO background polling: a timer that re-fetched page 1 while
// later pages stayed put shifted every offset as new runs arrived at the top, so
// appended pages re-loaded older runs that had already moved down (issue #46). Manual
// paging keeps the offsets stable for as long as the user is scrolling.
//
// The caller injects fetch (mirroring usePollingFeed/useQueueActions) so the paging
// logic is testable through this interface with a fake fetchFn and no timers.
export function usePagedRuns(fetchFn = fetch, options = {}) {
  const pageSize = options.pageSize ?? 10

  const runs = ref([])
  const error = ref(null)
  const isLoading = ref(false)
  const hasMore = ref(true)

  async function fetchPage(skip, take) {
    const res = await fetchFn(`/runs?skip=${skip}&take=${take}`)
    if (!res.ok) throw new Error(`HTTP ${res.status}`)
    return await res.json()
  }

  async function loadFirst() {
    isLoading.value = true
    try {
      const page = await fetchPage(0, pageSize)
      runs.value = page
      error.value = null
      hasMore.value = page.length >= pageSize
    } catch (e) {
      error.value = e.message
    } finally {
      isLoading.value = false
    }
  }

  // Append the next window after whatever is already loaded. No-ops once the end is
  // reached or while a page is already in flight (re-entrant guard).
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

  if (getCurrentInstance()) onMounted(loadFirst)

  return { runs, error, isLoading, hasMore, loadFirst, loadNext }
}
