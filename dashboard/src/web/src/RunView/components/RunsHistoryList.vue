<script setup>
import { computed } from "vue";
import ErrorBanner from "../../_shared/components/ErrorBanner.vue";
import RunCard from "./RunCard.vue";
import { isTerminalRun } from "../models/runStatus.js";
import { usePagedRuns } from "../services/usePagedRuns.js";
import { useNowTicker } from "../services/useNowTicker.js";

const { now } = useNowTicker();

// Deliberately not polled (issue #46): the raw /runs window drives the
// skip/take paging, and only terminal runs are shown.
const { runs, error, hasMore, isLoading, loadNext, refreshFirst } = usePagedRuns((url) =>
  fetch(url),
);

const historyRuns = computed(() => runs.value.filter(isTerminalRun));

// Called by the container when the active set changes (a run started or
// finished): re-fetches the first page once, leaving appended pages untouched.
defineExpose({ refreshFirst });
</script>

<template>
  <div>
    <ErrorBanner
      v-if="error"
      title="Connection lost"
      :message="`Failed to load run history: ${error}`"
    />

    <section class="history-runs" aria-label="Run history">
      <header class="section-header">
        <h2><span class="prompt">&gt;</span> History</h2>
      </header>

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
    </section>
  </div>
</template>

<style scoped>
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
.run-list {
  display: grid;
  gap: 1rem;
}
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
/* Skin lives in _shared/components/ErrorBanner.vue; these are this view's
   page-level geometry. */
.error-banner {
  margin-bottom: 1.5rem;
  padding: 1rem 1.25rem;
}
</style>
