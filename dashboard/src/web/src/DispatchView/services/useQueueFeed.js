import { usePollingFeed } from '../../_shared/services/usePollingFeed.js'

export function useQueueFeed(fetchFn, options = {}) {
  const { items: queue, error, isLoading, load, start, stop } = usePollingFeed(fetchFn, options)
  return { queue, error, isLoading, load, start, stop }
}
