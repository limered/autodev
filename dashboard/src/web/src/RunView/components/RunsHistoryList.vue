<script setup>
import ErrorBanner from "../../_shared/components/ErrorBanner.vue";
import RunCard from "./RunCard.vue";
import RunSection from "./RunSection.vue";
import { usePagedRuns } from "../services/usePagedRuns.js";
import { useNowTicker } from "../services/useNowTicker.js";

const { now } = useNowTicker();

// Deliberately not polled (issue #46): the raw /runs window drives the
// skip/take paging, and historyRuns is the terminal-only visible window.
const { historyRuns, error, hasMore, isLoading, loadNext, refreshFirst } = usePagedRuns((url) =>
  fetch(url),
);

// Called by the container when the active set changes (a run started or
// finished): re-fetches the first page once, leaving appended pages untouched.
defineExpose({ refreshFirst });
</script>

<template>
  <RunSection title="History" aria-label="Run history">
    <template #alerts>
      <ErrorBanner
        v-if="error"
        title="Connection lost"
        :message="`Failed to load run history: ${error}`"
      />
    </template>

    <div v-if="historyRuns.length" class="run-list">
      <RunCard v-for="r in historyRuns" :key="r.runId" :run="r" :now="now" />
    </div>

    <section v-else-if="!error" class="empty-state">
      <div class="empty-prompt">&gt;_</div>
      <h2>No finished runs yet</h2>
      <p>Runs will appear here as they finish.</p>
    </section>

    <div v-if="hasMore" class="load-more-row">
      <button type="button" class="load-more" :disabled="isLoading" @click="loadNext">
        {{ isLoading ? "Loading..." : "Load more" }}
      </button>
    </div>
  </RunSection>
</template>

<style scoped>
/* Single-use rows below the shared shell (header, list grid and banner
   geometry live in RunSection.vue): the empty state and the paging control. */
.empty-state {
  text-align: center;
  padding: 4rem 1rem;
  color: var(--text-muted);
}
.empty-prompt {
  font-family: var(--font-mono);
  font-size: 3rem;
  color: var(--accent-dim);
  margin-bottom: 0.5rem;
}
.empty-state h2 {
  margin: 0 0 0.5rem;
  color: var(--text);
  font-size: 1.25rem;
}
.empty-state p {
  margin: 0 auto;
  max-width: 24rem;
}
.load-more-row {
  display: flex;
  justify-content: center;
  padding: 1.5rem 0 0.5rem;
}
.load-more {
  padding: 0.6rem 1.4rem;
  font-size: 0.9rem;
  font-weight: 600;
  color: var(--text);
  background: var(--surface-2);
  border: 1px solid var(--border);
  border-radius: var(--radius);
  cursor: pointer;
  transition: border-color 0.2s ease;
}
.load-more:hover:not(:disabled) {
  border-color: var(--text-dim);
}
.load-more:disabled {
  opacity: 0.6;
  cursor: default;
}
</style>
