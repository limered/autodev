<script setup>
import { computed, ref, watch } from 'vue'
import { useIssuesFeed } from '../services/useIssuesFeed.js'
import { useQueueFeed } from '../services/useQueueFeed.js'
import { queueStatus, queueStatusClass } from '../models/queueView.js'

const issuesFeed = useIssuesFeed(() => fetch('/issues'))
const queueFeed = useQueueFeed(() => fetch('/queue'))

const { issues } = issuesFeed
const { queue } = queueFeed

const localQueue = ref([])
watch(
  queue,
  (newQueue) => {
    if (!isReordering.value && draggedId.value === null) {
      localQueue.value = [...newQueue]
    }
  },
  { immediate: true }
)

const enqueueError = ref(null)
const isEnqueueing = ref(false)

const queuedIssueIds = computed(() => new Set(queue.value.map(q => q.issueId)))
const eligibleIssues = computed(() =>
  issues.value.filter(issue => !queuedIssueIds.value.has(issue.githubId))
)

async function enqueue(issue) {
  if (isEnqueueing.value) return
  isEnqueueing.value = true
  enqueueError.value = null

  try {
    const res = await fetch('/queue', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ issueId: issue.githubId })
    })
    if (!res.ok) throw new Error(`HTTP ${res.status}`)

    await Promise.all([issuesFeed.load(), queueFeed.load()])
  } catch (e) {
    enqueueError.value = e.message
  } finally {
    isEnqueueing.value = false
  }
}

const reorderError = ref(null)
const isReordering = ref(false)
const draggedId = ref(null)
const dragOverId = ref(null)

function onDragStart(item, event) {
  draggedId.value = item.id
  event.dataTransfer.effectAllowed = 'move'
  event.dataTransfer.setData('text/plain', String(item.id))
}

function onDragOver(event, id) {
  event.preventDefault()
  dragOverId.value = id
}

function onDragLeave() {
  dragOverId.value = null
}

function onDragEnd() {
  draggedId.value = null
  dragOverId.value = null
}

async function onDrop(targetId) {
  dragOverId.value = null
  const sourceId = draggedId.value
  draggedId.value = null

  if (sourceId === null || sourceId === targetId) return

  const fromIndex = localQueue.value.findIndex(i => i.id === sourceId)
  const toIndex = localQueue.value.findIndex(i => i.id === targetId)
  if (fromIndex < 0 || toIndex < 0) return

  const reordered = [...localQueue.value]
  const [moved] = reordered.splice(fromIndex, 1)
  reordered.splice(toIndex, 0, moved)
  localQueue.value = reordered

  await persistOrder()
}

async function persistOrder() {
  isReordering.value = true
  reorderError.value = null

  try {
    const ids = localQueue.value.map(i => i.id)
    const res = await fetch('/queue/order', {
      method: 'PATCH',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ ids })
    })
    if (!res.ok) throw new Error(`HTTP ${res.status}`)
    await queueFeed.load()
  } catch (e) {
    reorderError.value = e.message
    await queueFeed.load()
  } finally {
    isReordering.value = false
  }
}

const removeError = ref(null)
const isRemoving = ref(false)

async function remove(item) {
  if (isRemoving.value) return
  isRemoving.value = true
  removeError.value = null

  try {
    const res = await fetch(`/queue/${item.id}`, { method: 'DELETE' })
    if (!res.ok) throw new Error(`HTTP ${res.status}`)
    await Promise.all([issuesFeed.load(), queueFeed.load()])
  } catch (e) {
    removeError.value = e.message
  } finally {
    isRemoving.value = false
  }
}
</script>

<template>
  <section class="dispatch-panel">
    <div class="dispatch-columns">
      <section class="eligible-column">
        <header class="panel-header">
          <h2><span class="prompt">&gt;</span> Eligible Issues</h2>
          <div class="connection" :class="{ loading: issuesFeed.isLoading.value }">
            <span class="connection-dot"></span>
            <span v-if="issuesFeed.error.value">sync error</span>
            <span v-else>live</span>
          </div>
        </header>

        <section v-if="issuesFeed.error.value" class="error-banner" role="alert">
          <strong>Sync failed</strong>
          <p>Failed to load eligible issues: {{ issuesFeed.error.value }}</p>
        </section>

        <section v-if="enqueueError" class="error-banner" role="alert">
          <strong>Enqueue failed</strong>
          <p>{{ enqueueError }}</p>
        </section>

        <section v-if="eligibleIssues.length" class="issue-list">
          <article
            v-for="issue in eligibleIssues"
            :key="issue.githubId"
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
          <div class="connection" :class="{ loading: queueFeed.isLoading.value || isReordering || isRemoving }">
            <span class="connection-dot"></span>
            <span v-if="queueFeed.error.value">sync error</span>
            <span v-else-if="isReordering || isRemoving">saving</span>
            <span v-else>live</span>
          </div>
        </header>

        <section v-if="queueFeed.error.value" class="error-banner" role="alert">
          <strong>Sync failed</strong>
          <p>Failed to load run queue: {{ queueFeed.error.value }}</p>
        </section>

        <section v-if="reorderError" class="error-banner" role="alert">
          <strong>Reorder failed</strong>
          <p>{{ reorderError }}</p>
        </section>

        <section v-if="removeError" class="error-banner" role="alert">
          <strong>Remove failed</strong>
          <p>{{ removeError }}</p>
        </section>

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
  grid-template-columns: 1fr 1fr;
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

.connection-dot {
  width: 0.4rem;
  height: 0.4rem;
  border-radius: 50%;
  background: var(--accent);
}

.error-banner {
  margin-bottom: 1rem;
  padding: 0.75rem 1rem;
  background: rgba(248, 81, 73, 0.12);
  border: 1px solid rgba(248, 81, 73, 0.35);
  border-radius: var(--radius);
  font-size: 0.9rem;
}

.error-banner strong {
  display: block;
  color: var(--red);
  margin-bottom: 0.25rem;
}

.error-banner p {
  margin: 0;
  color: var(--text);
}

.issue-list,
.queue-list {
  display: grid;
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
.status-running { color: var(--green); }
.status-running .status-indicator { animation: blink 1.4s infinite; }
.status-done { color: var(--blue); }
.status-failed { color: var(--red); }

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

@keyframes blink {
  0%, 100% { opacity: 1; }
  50% { opacity: 0.35; }
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