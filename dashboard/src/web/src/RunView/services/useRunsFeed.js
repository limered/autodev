import { ref, onMounted, onUnmounted, getCurrentInstance } from 'vue'

export function useRunsFeed(fetchFn, options = {}) {
  const interval = options.interval ?? 5000
  const runs = ref([])
  const error = ref(null)
  const isLoading = ref(false)
  let pollTimer = null

  async function load() {
    isLoading.value = true
    try {
      const res = await fetchFn()
      if (!res.ok) throw new Error(`HTTP ${res.status}`)
      runs.value = await res.json()
      error.value = null
    } catch (e) {
      error.value = e.message
    } finally {
      isLoading.value = false
    }
  }

  function start() {
    stop()
    load()
    pollTimer = setInterval(load, interval)
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

  return { runs, error, isLoading, load, start, stop }
}
