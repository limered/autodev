<script setup>
import ErrorBanner from "../../_shared/components/ErrorBanner.vue";
import RunCard from "./RunCard.vue";
import RunSection from "./RunSection.vue";
import { useActiveRuns } from "../services/useActiveRuns.js";
import { useRunActions } from "../services/useRunActions.js";
import { useNowTicker } from "../services/useNowTicker.js";

const emit = defineEmits(["set-change"]);

const { now } = useNowTicker();

// Polls the active set every ~5s (issue #52). The set-change event — a run
// started or finished — is the only signal sent upward: the container uses it
// to sync history's first page once and does nothing on ordinary polls.
const { runs, error, load } = useActiveRuns((url) => fetch(url), {
  onSetChange: () => emit("set-change"),
});

// Delete a stuck run (e.g. wedged in "launching"), then re-poll so it drops
// out of the list. History stays in sync through the service diff alone: the
// post-delete load() flows through usePollingFeed's onLoaded into the
// useActiveRuns id-set diff, which fires onSetChange exactly once when the set
// actually changed — no manual emit here (it would double-refresh on real
// changes and spuriously refresh when the set didn't change, e.g. deleting an
// already-dropped run).
const { deleteRun, deleteError } = useRunActions();
async function onDelete(runId) {
  if (await deleteRun(runId)) {
    await load();
  }
}
</script>

<template>
  <!-- Hidden entirely while the factory is idle; history's empty state tells
       the "nothing has happened yet" story. -->
  <RunSection title="Active" aria-label="Active runs" :hide-section="runs.length === 0">
    <template #alerts>
      <ErrorBanner
        v-if="error"
        title="Connection lost"
        :message="`Failed to load active runs: ${error}`"
      />

      <ErrorBanner
        v-if="deleteError"
        title="Delete failed"
        :message="`Could not delete run: ${deleteError}`"
      />
    </template>

    <div class="run-list">
      <RunCard v-for="r in runs" :key="r.runId" :run="r" :now="now" deletable @delete="onDelete" />
    </div>
  </RunSection>
</template>
