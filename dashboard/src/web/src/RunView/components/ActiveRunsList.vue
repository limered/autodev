<script setup>
import ErrorBanner from "../../_shared/components/ErrorBanner.vue";
import RunCard from "./RunCard.vue";
import { useActiveRuns } from "../services/useActiveRuns.js";
import { useNowTicker } from "../services/useNowTicker.js";

const emit = defineEmits(["set-change"]);

const { now } = useNowTicker();

// Polls the active set every ~5s (issue #52). The set-change event — a run
// started or finished — is the only signal sent upward: the container uses it
// to sync history's first page once and does nothing on ordinary polls.
const { runs, error } = useActiveRuns((url) => fetch(url), {
  onSetChange: () => emit("set-change"),
});
</script>

<template>
  <div>
    <ErrorBanner
      v-if="error"
      title="Connection lost"
      :message="`Failed to load active runs: ${error}`"
    />

    <!-- Hidden entirely while the factory is idle; history's empty state tells
         the "nothing has happened yet" story. -->
    <section v-if="runs.length" class="active-runs" aria-label="Active runs">
      <header class="section-header">
        <h2><span class="prompt">&gt;</span> Active</h2>
      </header>
      <div class="run-list">
        <RunCard v-for="r in runs" :key="r.runId" :run="r" :now="now" />
      </div>
    </section>
  </div>
</template>

<style scoped>
.active-runs {
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
h2 {
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
/* Skin lives in _shared/components/ErrorBanner.vue; these are this view's
   page-level geometry. */
.error-banner {
  margin-bottom: 1.5rem;
  padding: 1rem 1.25rem;
}
</style>
