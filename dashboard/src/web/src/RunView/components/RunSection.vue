<script setup>
// List-section shell shared by ActiveRunsList and RunsHistoryList (issue #104,
// Cards 2-3). The header, list grid and banner geometry lived copy-pasted in
// both lists; they live here once now, so a visual or spacing change to the
// shell edits one file. Only RunView uses it, so it stays in-theme next to
// RunCard — nothing moves to _shared/ speculatively.
//
// Slots: list rows go through the default slot (history's empty-state and
// load-more row ride along); each list's ErrorBanners go through the `alerts`
// slot above the section. Slot content is compiled in the consumer's scope,
// so the geometry below uses :slotted() — skin stays in ErrorBanner.vue,
// spacing lives here, keeping ErrorBanner's consumer-spacing pattern intact.
defineProps({
  // Section heading text, rendered after the `>` prompt.
  title: { type: String, required: true },
  // Accessible label for the <section>. Deliberately separate from the title:
  // "Active" labels "Active runs" while "History" labels "Run history".
  ariaLabel: { type: String, required: true },
  // Active hides the whole section while idle (its banners still render);
  // history always shows.
  hideSection: { type: Boolean, default: false },
});
</script>

<template>
  <slot name="alerts" />
  <section v-if="!hideSection" class="run-section" :aria-label="ariaLabel">
    <header class="section-header">
      <h2><span class="prompt">&gt;</span> {{ title }}</h2>
    </header>
    <slot />
  </section>
</template>

<style scoped>
/* Uniform section rhythm (previously `.active-runs`' bottom margin): separates
   the active section from history, and leaves the same trailing space after
   history, last on the page. */
.run-section {
  margin-bottom: 2rem;
}
.section-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
  margin-bottom: 1rem;
  padding-bottom: 0.75rem;
  border-bottom: 1px solid var(--border);
}
.section-header h2 {
  margin: 0;
  font-size: 1.1rem;
  font-weight: 700;
  letter-spacing: -0.01em;
}
.prompt {
  color: var(--accent);
  margin-right: 0.25rem;
}
/* Geometry for consumer-rendered slot content: the row grid and the
   page-level banner spacing (skin lives in
   _shared/components/ErrorBanner.vue). */
:slotted(.run-list) {
  display: grid;
  gap: 1rem;
}
:slotted(.error-banner) {
  margin-bottom: 1.5rem;
  padding: 1rem 1.25rem;
}
</style>
