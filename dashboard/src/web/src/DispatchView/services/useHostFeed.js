import { usePollingFeed } from '../../_shared/services/usePollingFeed.js'

export function useHostFeed(fetchFn, options = {}) {
  const { items: host, error, isLoading, load, start, stop } = usePollingFeed(fetchFn, options)
  return { host, error, isLoading, load, start, stop }
}
