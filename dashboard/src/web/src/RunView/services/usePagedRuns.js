import { getCurrentInstance, onMounted, ref } from "vue";

// RunView-local paged loader for the runs history list (issue #52 split). Loads
// the first 10 runs, then appends the next 10 each time loadNext() is called
// (driven by a "Load more" button). The caller filters rows to terminal runs
// for display, but offsets and hasMore are computed over the raw window the
// server returns, so the skip = runs.length paging math is untouched.
//
// There is deliberately NO background polling: a timer that re-fetched page 1
// while later pages stayed put shifted every offset as new runs arrived at the
// top, so appended pages re-loaded older runs that had already moved down
// (issue #46). refreshFirst() is the one sanctioned first-page sync: when the
// active list reports a run starting/finishing, the container calls it once —
// appended pages are left alone.
//
// The caller injects fetch (mirroring usePollingFeed/useQueueActions) so the paging
// logic is testable through this interface with a fake fetchFn and no timers.
export function usePagedRuns(fetchFn = fetch, options = {}) {
  const pageSize = options.pageSize ?? 10;

  const runs = ref([]);
  const error = ref(null);
  const isLoading = ref(false);
  const hasMore = ref(true);

  async function fetchPage(skip, take) {
    const res = await fetchFn(`/runs?skip=${skip}&take=${take}`);
    if (!res.ok) throw new Error(`HTTP ${res.status}`);
    return await res.json();
  }

  async function loadFirst() {
    isLoading.value = true;
    try {
      const page = await fetchPage(0, pageSize);
      runs.value = page;
      error.value = null;
      hasMore.value = page.length >= pageSize;
    } catch (e) {
      error.value = e.message;
    } finally {
      isLoading.value = false;
    }
  }

  // Re-fetch the first page and replace only those rows, leaving whatever was
  // appended after them in place (issue #52: history syncs its first page once
  // when the active set changes; it never polls on a timer). A full page keeps
  // the appended rows; a short page means the server's whole list now fits in
  // one window, so stale appended rows are dropped. With appended rows present
  // the refresh learned nothing new about the end, so a reached end stays
  // reached — "Load more" must not resurrect after e.g. a run merely finished.
  async function refreshFirst() {
    isLoading.value = true;
    try {
      const page = await fetchPage(0, pageSize);
      const fullPage = page.length >= pageSize;
      const rest = fullPage ? runs.value.slice(page.length) : [];
      runs.value = [...page, ...rest];
      error.value = null;
      hasMore.value = fullPage && (rest.length === 0 || hasMore.value);
    } catch (e) {
      error.value = e.message;
    } finally {
      isLoading.value = false;
    }
  }

  // Append the next window after whatever is already loaded. No-ops once the end is
  // reached or while a page is already in flight (re-entrant guard).
  async function loadNext() {
    if (!hasMore.value || isLoading.value) return;
    isLoading.value = true;
    try {
      const page = await fetchPage(runs.value.length, pageSize);
      runs.value = [...runs.value, ...page];
      error.value = null;
      if (page.length < pageSize) hasMore.value = false;
    } catch (e) {
      error.value = e.message;
    } finally {
      isLoading.value = false;
    }
  }

  if (getCurrentInstance()) onMounted(loadFirst);

  return { runs, error, isLoading, hasMore, loadFirst, loadNext, refreshFirst };
}
