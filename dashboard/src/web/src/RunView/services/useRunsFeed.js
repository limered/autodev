import { usePollingFeed } from '../../_shared/services/usePollingFeed.js'

export function useRunsFeed(fetchFn, options = {}) {
  const { items: runs, error, isLoading, load, start, stop } = usePollingFeed(fetchFn, options)
  return { runs, error, isLoading, load, start, stop }
}
