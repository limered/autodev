// Repo identity colors. Maps a repo slug (`owner/name`) to a stable color so
// the same repo reads the same way in the eligible-issues list and the run
// queue. Palette hues are curated to be legible on the dark surface and to
// stay clear of the status colors (accent/green, cyan, blue, red, amber)
// defined in App.vue's `:root`.
export const REPO_COLOR_PALETTE = [
  'var(--repo-violet)',
  'var(--repo-lilac)',
  'var(--repo-magenta)',
  'var(--repo-pink)',
  'var(--repo-orange)',
  'var(--repo-yellow)'
]

const FALLBACK_COLOR = 'var(--text-muted)'

// FNV-1a followed by a murmur-style avalanche, so the modulo below indexes
// with well-mixed bits for any palette size.
function hashRepo(repo) {
  let hash = 0x811c9dc5
  for (let i = 0; i < repo.length; i++) {
    hash ^= repo.charCodeAt(i)
    hash = Math.imul(hash, 0x01000193)
  }
  hash = Math.imul(hash ^ (hash >>> 16), 2246822507)
  hash = Math.imul(hash ^ (hash >>> 13), 3266489917)
  return (hash ^ (hash >>> 16)) >>> 0
}

export function repoColor(repo) {
  if (!repo) return FALLBACK_COLOR
  return REPO_COLOR_PALETTE[hashRepo(repo) % REPO_COLOR_PALETTE.length]
}
