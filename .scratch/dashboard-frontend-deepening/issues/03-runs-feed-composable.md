# 03 — Extract a runs-feed composable

**What to build:** The polling loop, loading flag, and error state move out of `App.vue` into a `useRunsFeed(fetchFn)` composable that owns the poll interval, the current runs, loading, and error. It's fed a fetch adapter so a fake feed can be substituted in tests, and `App.vue` just consumes the composable's state.

**Blocked by:** None — can start immediately.

**Status:** speculative

> Speculative (deferred): today there is only one adapter — the real `fetch`. One adapter is a hypothetical seam. Only pick this up once a second adapter (a fake feed for tests) is actually needed; until then it's a wrapper around a single implementation.

- [ ] `useRunsFeed(fetchFn)` owns poll interval, runs, loading, and error state
- [ ] `App.vue` consumes the composable rather than managing timers and fetch inline
- [ ] A fake `fetchFn` drives the composable in a test (success and error paths)
