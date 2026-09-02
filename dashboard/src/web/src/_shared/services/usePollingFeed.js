import { ref, onMounted, onUnmounted, getCurrentInstance } from "vue";

export function usePollingFeed(fetchFn, options = {}) {
  const interval = options.interval ?? 5000;
  const items = ref([]);
  const error = ref(null);
  const isLoading = ref(false);
  let pollTimer = null;

  async function load() {
    isLoading.value = true;
    try {
      const res = await fetchFn();
      if (!res.ok) throw new Error(`HTTP ${res.status}`);
      items.value = await res.json();
      error.value = null;
      // Post-load hook (used by useActiveRuns to diff the active set between
      // polls); fires only on success so a failed poll looks like no change.
      options.onLoaded?.(items.value);
    } catch (e) {
      error.value = e.message;
    } finally {
      isLoading.value = false;
    }
  }

  function start() {
    stop();
    load();
    pollTimer = setInterval(load, interval);
  }

  function stop() {
    if (pollTimer !== null) {
      clearInterval(pollTimer);
      pollTimer = null;
    }
  }

  if (getCurrentInstance()) {
    onMounted(start);
    onUnmounted(stop);
  }

  return { items, error, isLoading, load, start, stop };
}
