# 08 — Split the dispatch screen into Active Jobs (home) and Queue pages

**What to build:** the single three-column screen becomes two routed pages. `/` (home) shows **Active Jobs** (the runs list). `/queue` shows the **Queue** page (eligible issues + run queue). A persistent shell header switches between them.

**Blocked by:** 03 (queue must exist to have a Queue page).

**Status:** done

## Design decision

Explored three navigation models as a UI prototype (`?variant=A|B|C`, hosted in `App.vue`):

- **A — top tabs**
- **B — left sidebar rail with live counts**
- **C — terminal segmented switch with `[1]`/`[2]` hotkeys** ← chosen

**Verdict: Variant C.** It matches the existing `>_` terminal aesthetic; the segmented control sits in the header with keyboard hotkeys and the `factory:~/<page>$` prompt line reflects the active page. The losing variants and the switcher were throwaway prototype code and were not kept.

## What shipped

- [x] `vue-router` added; `/` → Active Jobs, `/queue` → Queue. Deep-links resolve via the existing SPA fallback (`Program.cs` `MapFallbackToFile("index.html")`).
- [x] The runs list is promoted out of `App.vue` into `RunView/components/RunsList.vue` (the `/` page body).
- [x] `DispatchView` (eligible issues + run queue) is the `/queue` page body, unchanged.
- [x] `App.vue` is now the shell: terminal prompt line + segmented `RouterLink` nav + `[1]`/`[2]` hotkeys (ignored while typing in inputs) + `<RouterView>`.
- [x] Global design tokens stay in `App.vue`; per-component scoped styles unchanged.
- [x] Build passes; all 25 web tests pass. Local flow unchanged.
