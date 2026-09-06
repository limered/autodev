<script setup>
import { computed } from "vue";
import ErrorBanner from "../../_shared/components/ErrorBanner.vue";
import { queueRowView } from "../models/queueView.js";
import { useQueueFeedShadow } from "../services/useQueueFeedShadow.js";
import { useQueueHandlers } from "../services/useQueueHandlers.js";
import IssueRef from "./IssueRef.vue";

// The run-queue column of the DispatchView split: owns everything its
// buttons do — the queue write actions and each action's run()-then-load()
// re-sync, wired once in the shared useQueueHandlers composable (Block A) —
// plus the drag-reorder interaction over the `localQueue` shadow copy of the
// feed, owned by useQueueFeedShadow. What still arrives as props is read
// state (the feed's items and sync error) plus the two feed loads the
// re-syncs need; no action state or handler passes through DispatchView.
const props = defineProps({
  // Array of queue items from the /queue feed, in server order.
  queue: { type: Array, required: true },
  // String error message from the /queue feed, or null while in sync.
  syncError: { type: String, default: null },
  // Loads the /issues feed; a remove re-syncs it too, since the issue
  // becomes eligible again.
  reloadIssues: { type: Function, required: true },
  // Loads the /queue feed; every queue write re-syncs it.
  reloadQueue: { type: Function, required: true },
});

// Wired queue handlers: every write re-syncs the feeds that show its effect —
// both feeds for a remove (the issue is eligible again), the queue alone for
// start-next and restart, the queue unconditionally after a reorder. Bound
// straight into the template below; the same contract is tested against the
// same composable in _tests/components/RunQueueColumn.test.js.
const {
  reorder,
  reorderError,
  remove,
  removeError,
  isRemoving,
  startNext,
  startNextError,
  isStartingNext,
  restart,
  restartError,
  isRestarting,
  isSaving,
} = useQueueHandlers({
  reloadIssues: props.reloadIssues,
  reloadQueue: props.reloadQueue,
});

const {
  localQueue,
  draggedId,
  dragOverId,
  onDragStart,
  onDragOver,
  onDragLeave,
  onDragEnd,
  onDrop,
} = useQueueFeedShadow({ feed: () => props.queue, isSaving, onReorder: reorder });

const nextQueueItem = computed(() => localQueue.value[0] ?? null);

// Single status-inference path for the gate and the rows below: every
// runId/runStatus branch funnels through queueRowView here, so the badge rows
// and the start-next gate can never drift apart. The gate still reads the
// feed (props.queue — server truth, since the shadow can lag mid-drag) while
// the rows render the shadow (localQueue).
const inferRowView = (item) => queueRowView(item);
const hasRunningItem = computed(() => props.queue.some((item) => inferRowView(item).isRunning));

// One row view-model per queue row (Block A): a single queueRowView call
// drives the status badge text and the Restart gating, instead of the
// template consulting 3-4 separate helpers per row. The status-* class is
// mapped inline in the template (`status-${view.status}`), not in the model.
const rows = computed(() => localQueue.value.map((item) => ({ item, view: inferRowView(item) })));

// Start-next gating is view state — the head of the shadow queue and whether
// anything is already running — so the guard stays here; the write and its
// re-sync are the wired `startNext` handler.
async function onStartNext() {
  const item = nextQueueItem.value;
  if (!item || hasRunningItem.value) return;
  await startNext(item);
}
</script>

<template>
  <section class="queue-column">
    <header class="panel-header">
      <h2><span class="prompt">&gt;</span> Run Queue</h2>
      <span v-if="isSaving" class="queue-saving">saving…</span>
    </header>

    <ErrorBanner
      v-if="syncError"
      title="Sync failed"
      :message="`Failed to load run queue: ${syncError}`"
    />

    <ErrorBanner v-if="reorderError" title="Reorder failed" :message="reorderError" />

    <ErrorBanner v-if="removeError" title="Remove failed" :message="removeError" />

    <ErrorBanner v-if="startNextError" title="Start failed" :message="startNextError" />

    <ErrorBanner v-if="restartError" title="Restart failed" :message="restartError" />

    <button
      class="start-next-button"
      :disabled="!nextQueueItem || hasRunningItem || isStartingNext"
      @click="onStartNext"
    >
      {{ isStartingNext ? "Starting…" : "Start next" }}
    </button>

    <section v-if="localQueue.length" class="queue-list">
      <article
        v-for="{ item, view } in rows"
        :key="item.id"
        class="queue-row"
        :class="{ dragging: draggedId === item.id, 'drag-over': dragOverId === item.id }"
        draggable="true"
        @dragstart="onDragStart(item, $event)"
        @dragover="onDragOver($event, item.id)"
        @dragleave="onDragLeave"
        @drop="onDrop(item.id)"
        @dragend="onDragEnd"
      >
        <span class="queue-rank mono">#{{ item.rank }}</span>
        <div class="queue-meta">
          <IssueRef
            v-if="item.issuePresent"
            :html-url="item.htmlUrl"
            :title="item.title"
            :repo="item.repo"
            :number="item.number"
          />
          <template v-else>
            <span class="missing-issue">no longer eligible</span>
          </template>
        </div>
        <span class="status-badge" :class="`status-${view.status}`">
          <span class="status-indicator"></span>
          {{ view.status }}
        </span>
        <button
          v-if="view.isFailed"
          class="restart-button"
          :disabled="isRestarting"
          @click="restart(item)"
        >
          Restart
        </button>
        <button class="remove-button" :disabled="isRemoving" @click="remove(item)">Remove</button>
      </article>
    </section>

    <section v-else-if="!syncError" class="empty-state">
      <div class="empty-prompt">&gt;_</div>
      <p>The queue is empty.</p>
    </section>
  </section>
</template>

<style scoped>
.queue-column {
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

.queue-saving {
  font-size: 0.7rem;
  color: var(--text-muted);
  text-transform: uppercase;
  letter-spacing: 0.05em;
}

/* Skin lives in _shared/components/ErrorBanner.vue; these are this view's
   compact in-column geometry. */
.error-banner {
  margin-bottom: 1rem;
  padding: 0.75rem 1rem;
  font-size: 0.9rem;
}

.queue-list {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.queue-row {
  display: flex;
  align-items: center;
  min-width: 0;
  gap: 0.75rem;
  padding: 0.75rem 1rem;
  background: var(--surface);
  border: 1px solid var(--border);
  border-radius: var(--radius);
  cursor: grab;
  transition:
    border-color 0.15s ease,
    opacity 0.15s ease;
}

.queue-row:hover {
  border-color: var(--text-dim);
}

.queue-row.dragging {
  opacity: 0.4;
}

.queue-row.drag-over {
  border-color: var(--accent);
}

.queue-rank {
  flex-shrink: 0;
  width: 2rem;
  font-size: 0.85rem;
  color: var(--text-dim);
}

.queue-meta {
  display: flex;
  flex-direction: column;
  min-width: 0;
  gap: 0.15rem;
}

.missing-issue {
  color: var(--text-muted);
  font-style: italic;
  text-decoration: line-through;
}

.status-badge {
  display: inline-flex;
  align-items: center;
  gap: 0.4rem;
  flex-shrink: 0;
  padding: 0.35rem 0.7rem;
  border-radius: 999px;
  font-size: 0.8rem;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.04em;
  background: var(--surface);
  border: 1px solid currentColor;
}

.status-indicator {
  width: 0.5rem;
  height: 0.5rem;
  border-radius: 50%;
  background: currentColor;
}

.status-queued {
  color: var(--text-muted);
}
.status-starting {
  color: var(--cyan);
}
.status-starting .status-indicator {
  animation: blink 1.4s infinite;
}
.status-running {
  color: var(--green);
}
.status-running .status-indicator {
  animation: blink 1.4s infinite;
}
.status-done {
  color: var(--blue);
}
.status-failed {
  color: var(--red);
}

.start-next-button {
  width: 100%;
  margin-bottom: 1rem;
  padding: 0.6rem 1rem;
  background: var(--surface);
  border: 1px solid var(--accent);
  border-radius: var(--radius);
  color: var(--accent);
  font-size: 0.9rem;
  font-weight: 600;
  cursor: pointer;
  transition:
    background 0.15s ease,
    color 0.15s ease;
}

.start-next-button:hover:not(:disabled) {
  background: var(--accent);
  color: var(--background);
}

.start-next-button:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

.remove-button {
  flex-shrink: 0;
  padding: 0.3rem 0.6rem;
  background: transparent;
  border: 1px solid var(--border);
  border-radius: var(--radius);
  color: var(--text-muted);
  font-size: 0.75rem;
  font-weight: 600;
  cursor: pointer;
  transition:
    background 0.15s ease,
    border-color 0.15s ease,
    color 0.15s ease;
}

.remove-button:hover:not(:disabled) {
  color: var(--red);
  border-color: var(--red);
  background: rgba(248, 81, 73, 0.08);
}

.remove-button:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

.restart-button {
  flex-shrink: 0;
  padding: 0.3rem 0.6rem;
  background: transparent;
  border: 1px solid var(--border);
  border-radius: var(--radius);
  color: var(--accent);
  font-size: 0.75rem;
  font-weight: 600;
  cursor: pointer;
  transition:
    background 0.15s ease,
    border-color 0.15s ease,
    color 0.15s ease;
}

.restart-button:hover:not(:disabled) {
  background: var(--accent);
  border-color: var(--accent);
  color: var(--background);
}

.restart-button:disabled {
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

/* The issue-ref's mono font lives in IssueRef.vue now; this rule stays
   because .queue-rank still wears the .mono class. */
.mono {
  font-family: var(--font-mono);
}

@keyframes blink {
  0%,
  100% {
    opacity: 1;
  }
  50% {
    opacity: 0.35;
  }
}

@media (max-width: 640px) {
  .queue-row {
    flex-wrap: wrap;
  }
}
</style>
