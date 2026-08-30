import { getCurrentInstance, onMounted, onUnmounted, ref } from 'vue'

// Shared 1s "now" ticker for the run lists (active + history both own one):
// runView() derives live freshness labels from `now`, so cards re-render their
// "last seen" as time passes. One interval per list component, not per card.
export function useNowTicker(intervalMs = 1000) {
  const now = ref(Date.now())
  let timer = null

  function start() {
    stop()
    timer = setInterval(() => { now.value = Date.now() }, intervalMs)
  }

  function stop() {
    if (timer !== null) {
      clearInterval(timer)
      timer = null
    }
  }

  if (getCurrentInstance()) {
    onMounted(start)
    onUnmounted(stop)
  }

  return { now, start, stop }
}
