<script setup>
import { computed, ref, watch } from 'vue'
import { usePollingFeed } from '../../_shared/services/usePollingFeed.js'
import ErrorBanner from '../../_shared/components/ErrorBanner.vue'
import { useQueueActions } from '../services/useQueueActions.js'
import { useDragReorder } from '../services/useDragReorder.js'
import { queueStatus, queueStatusClass, isQueueItemRunning, isQueueItemFailed } from '../models/queueView.js'
import { hostView } from '../models/hostView.js'

const issuesFeed = usePollingFeed(() => fetch('/issues'))
const queueFeed = usePollingFeed(() => fetch('/queue'))
const hostFeed = usePollingFeed(() => fetch('/host'))

const { items: issues } = issuesFeed
const { items: queue } = queueFeed
const { items: host } = hostFeed

const actions = useQueueActions()
const {
  enqueueError, isEnqueueing,
  reorderError, isReordering,
  removeError, isRemoving,
  startNextError, isStartingNext,
  restartError, isRestarting
} = actions

const localQueue = ref([])

const {
  draggedId, dragOverId,
  onDragStart, onDragOver, onDragLeave, onDragEnd, onDrop
} = useDragReorder({
  items: localQueue,
  onReorder: async (ids) => {
    await actions.reorder(ids)
    await queueFeed.load()
  }
})

watch(
  queue,
  (newQueue) => {
    if (!isReordering.value && draggedId.value === null) {
      localQueue.value = [...newQueue]
    }
  },
  { immediate: true }
)

const queuedIssueIds = computed(() => new Set(queue.value.map(q => q.issueId)))
const eligibleIssues = computed(() =>
  issues.value.filter(issue => !queuedIssueIds.value.has(issue.gitHubId))
)
const hasRunningItem = computed(() => queue.value.some(isQueueItemRunning))
const nextQueueItem = computed(() => localQueue.value[0] ?? null)
const isSaving = computed(() => isReordering.value || isRemoving.value || isRestarting.value)
const connectionError = computed(() => issuesFeed.error.value || queueFeed.error.value)
const isPollingLoading = computed(() => issuesFeed.isLoading.value || queueFeed.isLoading.value)
const hostBadge = computed(() => hostView(host.value, Date.now()))

async function enqueue(issue) {
  if (await actions.enqueue(issue.gitHubId)) {
    await Promise.all([issuesFeed.load(), queueFeed.load()])
  }
}

async function remove(item) {
  if (await actions.remove(item.id)) {
    await Promise.all([issuesFeed.load(), queueFeed.load()])
  }
}

async function startNext() {
  const item = nextQueueItem.value
  if (!item || hasRunningItem.value) return
  if (await actions.startNext(item.id)) {
    await queueFeed.load()
  }
}

async function restart(item) {
  if (await actions.restart(item.id)) {
    await queueFeed.load()
  }
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
          <span v-if="!hostBadge.online" class="host-last-seen">(last seen {{ hostBadge.lastSeen }})</span>
        </div>
      </div>
    </header>

    <div class="dispatch-columns">
      <section class="eligible-column">
        <header class="panel-header">
          <h2><span class="prompt">&gt;</span> Eligible Issues</h2>
        </header>

        <ErrorBanner
          v-if="issuesFeed.error.value"
          title="Sync failed"
          :message="`Failed to load eligible issues: ${issuesFeed.error.value}`"
        />

        <ErrorBanner v-if="enqueueError" title="Enqueue failed" :message="enqueueError" />

        <section v-if="eligibleIssues.length" class="issue-list">
          <article
            v-for="issue in eligibleIssues"
            :key="issue.gitHubId"
            class="issue-row"
          >
            <div class="issue-meta">
              <a :href="issue.htmlUrl" target="_blank" rel="noopener" class="issue-title">
                {{ issue.title }}
              </a>
              <span class="issue-ref mono">{{ issue.repo }}#{{ issue.number }}</span>
            </div>
            <button
              class="enqueue-button"
              :disabled="isEnqueueing"
              @click="enqueue(issue)"
            >
              Enqueue →
            </button>
          </article>
        </section>

        <section v-else-if="!issuesFeed.error.value" class="empty-state">
          <div class="empty-prompt">&gt;_</div>
          <p v-if="issues.length">All eligible issues are queued.</p>
          <p v-else>No eligible issues synced yet.</p>
        </section>
      </section>

      <section class="queue-column">
        <header class="panel-header">
          <h2><span class="prompt">&gt;</span> Run Queue</h2>
          <span v-if="isSaving" class="queue-saving">saving…</span>
        </header>

        <ErrorBanner
          v-if="queueFeed.error.value"
          title="Sync failed"
          :message="`Failed to load run queue: ${queueFeed.error.value}`"
        />

        <ErrorBanner v-if="reorderError" title="Reorder failed" :message="reorderError" />

        <ErrorBanner v-if="removeError" title="Remove failed" :message="removeError" />

        <ErrorBanner v-if="startNextError" title="Start failed" :message="startNextError" />

        <ErrorBanner v-if="restartError" title="Restart failed" :message="restartError" />

        <button
          class="start-next-button"
          :disabled="!nextQueueItem || hasRunningItem || isStartingNext"
          @click="startNext"
        >
          {{ isStartingNext ? 'Starting…' : 'Start next' }}
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
                <span class="issue-ref mono">{{ item.repo }}#{{ item.number }}</span>
              </template>
              <template v-else>
                <span class="missing-issue">no longer eligible</span>
              </template>
            </div>
            <span class="status-badge" :class="queueStatusClass(item)">
              <span class="status-indicator"></span>
              {{ queueStatus(item) }}
            </span>
            <a
              v-if="item.runId"
              :href="`/#run-${item.runId}`"
              class="run-link mono"
            >
              Run ↗
            </a>
            <button
              v-if="isQueueItemFailed(item)"
              class="restart-button"
              :disabled="isRestarting"
              @click="restart(item)"
            >
              Restart
            </button>
            <button
              class="remove-button"
              :disabled="isRemoving"
              @click="remove(item)"
            >
              Remove
            </button>
          </article>
        </section>

        <section v-else-if="!queueFeed.error.value" class="empty-state">
          <div class="empty-prompt">&gt;_</div>
          <p>The queue is empty.</p>
        </section>
      </section>
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

.eligible-column,
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

.dispatch-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
  margin-bottom: 1rem;
  padding-bottom: 0.75rem;
  border-bottom: 1px solid var(--border);
}

.dispatch-indicators {
  display: flex;
  align-items: center;
  gap: 0.75rem;
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

.queue-saving {
  font-size: 0.7rem;
  color: var(--text-muted);
  text-transform: uppercase;
  letter-spacing: 0.05em;
}

.connection-dot {
  width: 0.4rem;
  height: 0.4rem;
  border-radius: 50%;
  background: var(--accent);
}

/* Skin lives in _shared/components/ErrorBanner.vue; these are this view's
   compact in-column geometry. */
.error-banner {
  margin-bottom: 1rem;
  padding: 0.75rem 1rem;
  font-size: 0.9rem;
}

.issue-list,
.queue-list {
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

.issue-meta,
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
  transition: background 0.15s ease, border-color 0.15s ease;
}

.enqueue-button:hover:not(:disabled) {
  background: var(--surface);
  border-color: var(--accent-dim);
}

.enqueue-button:disabled {
  opacity: 0.6;
  cursor: not-allowed;
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
  transition: border-color 0.15s ease, opacity 0.15s ease;
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

.missing-issue {
  color: var(--text-muted);
  font-style: italic;
  text-decoration: line-through;
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

.status-queued { color: var(--text-muted); }
.status-starting { color: var(--cyan); }
.status-starting .status-indicator { animation: blink 1.4s infinite; }
.status-running { color: var(--green); }
.status-running .status-indicator { animation: blink 1.4s infinite; }
.status-done { color: var(--blue); }
.status-failed { color: var(--red); }

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
  transition: background 0.15s ease, color 0.15s ease;
}

.start-next-button:hover:not(:disabled) {
  background: var(--accent);
  color: var(--background);
}

.start-next-button:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

.run-link {
  flex-shrink: 0;
  font-size: 0.8rem;
  color: var(--accent);
  text-decoration: none;
}

.run-link:hover {
  text-decoration: underline;
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
  transition: background 0.15s ease, border-color 0.15s ease, color 0.15s ease;
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
  transition: background 0.15s ease, border-color 0.15s ease, color 0.15s ease;
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

@keyframes blink {
  0%, 100% { opacity: 1; }
  50% { opacity: 0.35; }
}

@keyframes pulse {
  0%, 100% { opacity: 1; }
  50% { opacity: 0.6; }
}

@media (max-width: 640px) {
  .issue-row {
    flex-direction: column;
    align-items: flex-start;
    gap: 0.5rem;
  }

  .issue-title {
    white-space: normal;
  }

  .queue-row {
    flex-wrap: wrap;
  }
}
</style>