<script setup>
import { computed, ref, watch } from "vue";
import ErrorBanner from "../../_shared/components/ErrorBanner.vue";
import { repoColor } from "../../_shared/models/repoColor.js";
import { useDragReorder } from "../services/useDragReorder.js";
import {
  queueStatus,
  queueStatusClass,
  isQueueItemRunning,
  isQueueItemFailed,
} from "../models/queueView.js";

// The run-queue column of the DispatchView split: owns the drag-to-reorder
// interaction and the `localQueue` shadow copy of the feed, so a drop is
// reflected instantly while the persist + re-sync round-trip runs in the
// orchestrator. Props are the feed and the per-action state wired down from
// DispatchView; everything drag-related stays local to this column.
const props = defineProps({
  // Array of queue items from the /queue feed, in server order.
  queue: { type: Array, required: true },
  // String error message from the /queue feed, or null while in sync.
  syncError: { type: String, default: null },
  // String error message from the last reorder attempt, or null.
  reorderError: { type: String, default: null },
  // String error message from the last remove attempt, or null.
  removeError: { type: String, default: null },
  // String error message from the last start-next attempt, or null.
  startNextError: { type: String, default: null },
  // String error message from the last restart attempt, or null.
  restartError: { type: String, default: null },
  // True while a reorder request is in flight.
  isReordering: { type: Boolean, default: false },
  // True while a remove request is in flight.
  isRemoving: { type: Boolean, default: false },
  // True while a start-next request is in flight.
  isStartingNext: { type: Boolean, default: false },
  // True while a restart request is in flight.
  isRestarting: { type: Boolean, default: false },
});

// `reorder` carries the new id order to persist; `remove` and `restart`
// carry the affected queue item; `start-next` carries the queue head to
// start. The orchestrator owns the requests and the feed refresh.
const emit = defineEmits(["reorder", "remove", "restart", "start-next"]);

// Shadow copy of the feed queue: drops land here first for instant
// feedback, and the feed only overwrites it while no save or drag is in
// flight, so a polling sync never yanks rows out from under the pointer.
const localQueue = ref([]);

const { draggedId, dragOverId, onDragStart, onDragOver, onDragLeave, onDragEnd, onDrop } =
  useDragReorder({
    items: localQueue,
    onReorder: (ids) => emit("reorder", ids),
  });

watch(
  () => props.queue,
  (newQueue) => {
    if (!props.isReordering && draggedId.value === null) {
      localQueue.value = [...newQueue];
    }
  },
  { immediate: true },
);

const nextQueueItem = computed(() => localQueue.value[0] ?? null);
const hasRunningItem = computed(() => props.queue.some(isQueueItemRunning));
const isSaving = computed(() => props.isReordering || props.isRemoving || props.isRestarting);

function onStartNext() {
  const item = nextQueueItem.value;
  if (!item || hasRunningItem.value) return;
  emit("start-next", item);
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
        v-for="item in localQueue"
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
          <template v-if="item.issuePresent">
            <a :href="item.htmlUrl" target="_blank" rel="noopener" class="issue-title">
              {{ item.title }}
            </a>
            <span class="issue-ref mono">
              <span class="issue-repo" :style="{ color: repoColor(item.repo) }">{{
                item.repo
              }}</span
              >#{{ item.number }}
            </span>
          </template>
          <template v-else>
            <span class="missing-issue">no longer eligible</span>
          </template>
        </div>
        <span class="status-badge" :class="queueStatusClass(item)">
          <span class="status-indicator"></span>
          {{ queueStatus(item) }}
        </span>
        <button
          v-if="isQueueItemFailed(item)"
          class="restart-button"
          :disabled="isRestarting"
          @click="emit('restart', item)"
        >
          Restart
        </button>
        <button class="remove-button" :disabled="isRemoving" @click="emit('remove', item)">
          Remove
        </button>
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

.issue-title {
  color: var(--text);
  text-decoration: none;
  font-weight: 500;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.issue-title:hover {
  text-decoration: underline;
}

.issue-ref {
  font-size: 0.8rem;
  color: var(--text-muted);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
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
