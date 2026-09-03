<script setup>
import { computed } from "vue";
import ErrorBanner from "../../_shared/components/ErrorBanner.vue";
import { eligibleIssues } from "../models/issuesView.js";
import { useQueueActions } from "../services/useQueueActions.js";
import IssueRef from "./IssueRef.vue";

// The eligible-issues column of the DispatchView split: renders the synced
// open issues that are not already queued and owns what its button does —
// the enqueue write (useQueueActions) and its run()-then-load() re-sync.
// Read state still arrives as props (`issues` is the whole synced set and
// `queuedIssueIds` the ids already in the run queue, wired cross-feed by the
// orchestrator) plus the two feed loads the re-sync needs; no action state
// or handler passes through DispatchView.
const props = defineProps({
  // Array of open issues from the /issues feed.
  issues: { type: Array, required: true },
  // Set of gitHubIds already present in the run queue; those issues are excluded.
  queuedIssueIds: { type: Set, default: () => new Set() },
  // String error message from the /issues feed, or null while in sync.
  syncError: { type: String, default: null },
  // Loads the /issues feed, for the re-sync after an enqueue.
  reloadIssues: { type: Function, required: true },
  // Loads the /queue feed, for the re-sync after an enqueue (the queued ids change).
  reloadQueue: { type: Function, required: true },
});

const { enqueue, enqueueError, isEnqueueing } = useQueueActions();

// Which synced issues count as eligible is pure view logic, so it lives in
// models/issuesView.js (table-tested there); this computed just re-derives
// it when either feed moves.
const eligible = computed(() => eligibleIssues(props.issues, props.queuedIssueIds));

// The Enqueue button's write-then-resync: run the write, then refresh both
// feeds it affects — the queue gains a row and the synced issue set may
// move. Mirror of the write-then-resync seam tests in
// _tests/components/EligibleIssuesColumn.test.js.
async function onEnqueue(issue) {
  if (await enqueue(issue.gitHubId)) {
    await Promise.all([props.reloadIssues(), props.reloadQueue()]);
  }
}
</script>

<template>
  <section class="eligible-column">
    <header class="panel-header">
      <h2><span class="prompt">&gt;</span> Eligible Issues</h2>
    </header>

    <ErrorBanner
      v-if="syncError"
      title="Sync failed"
      :message="`Failed to load eligible issues: ${syncError}`"
    />

    <ErrorBanner v-if="enqueueError" title="Enqueue failed" :message="enqueueError" />

    <section v-if="eligible.length" class="issue-list">
      <article v-for="issue in eligible" :key="issue.gitHubId" class="issue-row">
        <div class="issue-meta">
          <IssueRef
            :html-url="issue.htmlUrl"
            :title="issue.title"
            :repo="issue.repo"
            :number="issue.number"
          />
        </div>
        <button class="enqueue-button" :disabled="isEnqueueing" @click="onEnqueue(issue)">
          Enqueue →
        </button>
      </article>
    </section>

    <section v-else-if="!syncError" class="empty-state">
      <div class="empty-prompt">&gt;_</div>
      <p v-if="issues.length">All eligible issues are queued.</p>
      <p v-else>No eligible issues synced yet.</p>
    </section>
  </section>
</template>

<style scoped>
.eligible-column {
  display: flex;
  flex-direction: column;
  min-width: 0;
  min-height: 0;
}

.panel-header {
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

/* Skin lives in _shared/components/ErrorBanner.vue; these are this view's
   compact in-column geometry. */
.error-banner {
  margin-bottom: 1rem;
  padding: 0.75rem 1rem;
  font-size: 0.9rem;
}

.issue-list {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.issue-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
  padding: 0.75rem 1rem;
  background: var(--surface);
  border: 1px solid var(--border);
  border-radius: var(--radius);
}

.issue-row:hover {
  border-color: var(--text-dim);
}

.issue-meta {
  display: flex;
  flex-direction: column;
  min-width: 0;
  gap: 0.15rem;
}

.enqueue-button {
  flex-shrink: 0;
  padding: 0.4rem 0.75rem;
  background: var(--surface-2);
  border: 1px solid var(--border);
  border-radius: var(--radius);
  color: var(--accent);
  font-size: 0.8rem;
  font-weight: 600;
  cursor: pointer;
  transition:
    background 0.15s ease,
    border-color 0.15s ease;
}

.enqueue-button:hover:not(:disabled) {
  background: var(--surface);
  border-color: var(--accent-dim);
}

.enqueue-button:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

.empty-state {
  text-align: center;
  padding: 2rem 1rem;
  color: var(--text-muted);
}

.empty-prompt {
  font-family: var(--font-mono);
  font-size: 2rem;
  color: var(--accent-dim);
  margin-bottom: 0.25rem;
}

.empty-state p {
  margin: 0;
}

@media (max-width: 640px) {
  .issue-row {
    flex-direction: column;
    align-items: flex-start;
    gap: 0.5rem;
  }

  /* The title link lives in IssueRef now, so reaching it needs :deep();
     this column's mobile behaviour is to let the wrapped row's title wrap
     instead of ellipsizing. */
  :deep(.issue-title) {
    white-space: normal;
  }
}
</style>
