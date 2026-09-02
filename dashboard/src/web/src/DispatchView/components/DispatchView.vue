<script setup>
import { computed } from "vue";
import { usePollingFeed } from "../../_shared/services/usePollingFeed.js";
import { hostView } from "../models/hostView.js";
import { useQueueActions } from "../services/useQueueActions.js";
import EligibleIssuesColumn from "./EligibleIssuesColumn.vue";
import RunQueueColumn from "./RunQueueColumn.vue";

// Composition layer for the dispatch split: owns the three feeds and the
// queue write actions (useQueueActions), and wires them into the two
// columns. The eligible column owns the eligibility filter; the queue
// column owns the drag-reorder interaction and its localQueue shadow copy.
// The columns share almost no state, so each keeps its own test surface
// (enqueue vs drag-reorder, covered in _tests/components/).
const issuesFeed = usePollingFeed(() => fetch("/issues"));
const queueFeed = usePollingFeed(() => fetch("/queue"));
const hostFeed = usePollingFeed(() => fetch("/host"));

const { items: issues } = issuesFeed;
const { items: queue } = queueFeed;
const { items: host } = hostFeed;

const actions = useQueueActions();
const {
  enqueueError,
  isEnqueueing,
  reorderError,
  isReordering,
  removeError,
  isRemoving,
  startNextError,
  isStartingNext,
  restartError,
  isRestarting,
} = actions;

// The eligible column's filter input: which synced issues are already queued.
const queuedIssueIds = computed(() => new Set(queue.value.map((q) => q.issueId)));

const isSaving = computed(() => isReordering.value || isRemoving.value || isRestarting.value);
const connectionError = computed(() => issuesFeed.error.value || queueFeed.error.value);
const isPollingLoading = computed(() => issuesFeed.isLoading.value || queueFeed.isLoading.value);
const hostBadge = computed(() => hostView(host.value, Date.now()));

// Column events land here: run the write action, then refresh whichever
// feeds show its effect. Reorder re-syncs the queue unconditionally so the
// column's shadow copy converges on server truth even when the persist failed.
async function enqueue(issue) {
  if (await actions.enqueue(issue.gitHubId)) {
    await Promise.all([issuesFeed.load(), queueFeed.load()]);
  }
}

async function remove(item) {
  if (await actions.remove(item.id)) {
    await Promise.all([issuesFeed.load(), queueFeed.load()]);
  }
}

async function startNext(item) {
  if (await actions.startNext(item.id)) {
    await queueFeed.load();
  }
}

async function restart(item) {
  if (await actions.restart(item.id)) {
    await queueFeed.load();
  }
}

async function reorder(ids) {
  await actions.reorder(ids);
  await queueFeed.load();
}
</script>

<template>
  <section class="dispatch-panel">
    <header class="dispatch-header">
      <h2><span class="prompt">&gt;</span> Dispatch</h2>
      <div class="dispatch-indicators">
        <div class="connection" :class="{ loading: isPollingLoading || isSaving }">
          <span class="connection-dot"></span>
          <span v-if="connectionError">sync error</span>
          <span v-else-if="isSaving">saving</span>
          <span v-else>live</span>
        </div>
        <div class="connection host-badge" :class="hostBadge.freshnessClass">
          <span class="connection-dot"></span>
          <span>{{ hostBadge.label }}</span>
          <span v-if="!hostBadge.online" class="host-last-seen"
            >(last seen {{ hostBadge.lastSeen }})</span
          >
        </div>
      </div>
    </header>

    <div class="dispatch-columns">
      <EligibleIssuesColumn
        :issues="issues"
        :queued-issue-ids="queuedIssueIds"
        :sync-error="issuesFeed.error.value"
        :enqueue-error="enqueueError"
        :is-enqueueing="isEnqueueing"
        @enqueue="enqueue"
      />

      <RunQueueColumn
        :queue="queue"
        :sync-error="queueFeed.error.value"
        :reorder-error="reorderError"
        :remove-error="removeError"
        :start-next-error="startNextError"
        :restart-error="restartError"
        :is-reordering="isReordering"
        :is-removing="isRemoving"
        :is-starting-next="isStartingNext"
        :is-restarting="isRestarting"
        @reorder="reorder"
        @remove="remove"
        @restart="restart"
        @start-next="startNext"
      />
    </div>
  </section>
</template>

<style scoped>
.dispatch-panel {
  display: flex;
  flex-direction: column;
  min-height: 0;
}

.dispatch-columns {
  display: grid;
  grid-template-columns: minmax(0, 1fr) minmax(0, 1fr);
  gap: 1.5rem;
}

@media (max-width: 900px) {
  .dispatch-columns {
    grid-template-columns: 1fr;
  }
}

.dispatch-header {
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

.dispatch-indicators {
  display: flex;
  align-items: center;
  gap: 0.75rem;
}

.connection {
  display: inline-flex;
  align-items: center;
  gap: 0.5rem;
  padding: 0.25rem 0.6rem;
  background: var(--surface);
  border: 1px solid var(--border);
  border-radius: 999px;
  font-size: 0.7rem;
  color: var(--text-muted);
  text-transform: uppercase;
  letter-spacing: 0.05em;
}

.connection.loading {
  animation: pulse 1.4s infinite;
}

.host-badge.fresh {
  color: var(--green);
  border-color: var(--green);
}

.host-badge.fresh .connection-dot {
  background: var(--green);
}

.host-badge.stale-danger,
.host-badge.unknown {
  color: var(--red);
  border-color: var(--red);
}

.host-badge.stale-danger .connection-dot,
.host-badge.unknown .connection-dot {
  background: var(--red);
}

.host-last-seen {
  text-transform: none;
  letter-spacing: 0;
}

.connection-dot {
  width: 0.4rem;
  height: 0.4rem;
  border-radius: 50%;
  background: var(--accent);
}

@keyframes pulse {
  0%,
  100% {
    opacity: 1;
  }
  50% {
    opacity: 0.6;
  }
}
</style>
