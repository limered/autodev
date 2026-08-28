import { usePollingFeed } from '../../_shared/services/usePollingFeed.js'

export function useIssuesFeed(fetchFn, options = {}) {
  const { items: issues, error, isLoading, load, start, stop } = usePollingFeed(fetchFn, options)
  return { issues, error, isLoading, load, start, stop }
}
