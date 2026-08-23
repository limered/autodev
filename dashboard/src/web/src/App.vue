<script setup>
import { ref, onMounted, onUnmounted } from 'vue'

const runs = ref([])
const error = ref(null)
const now = ref(Date.now())
const isLoading = ref(false)
let pollTimer = null
let tickTimer = null

async function load() {
  isLoading.value = true
  try {
    const res = await fetch('/runs')
    if (!res.ok) throw new Error(`HTTP ${res.status}`)
    runs.value = await res.json()
    error.value = null
  } catch (e) {
    error.value = e.message
  } finally {
    isLoading.value = false
  }
}

function secondsSince(ts) {
  if (!ts) return null
  return Math.max(0, Math.round((now.value - new Date(ts).getTime()) / 1000))
}

function lastSeen(r) {
  const secs = secondsSince(r.lastHeartbeatAt)
  if (secs === null) return '—'
  if (secs < 60) return `${secs}s ago`
  if (secs < 3600) return `${Math.floor(secs / 60)}m ${secs % 60}s ago`
  return `${Math.floor(secs / 3600)}h ${Math.floor((secs % 3600) / 60)}m ago`
}

function freshnessClass(r) {
  const secs = secondsSince(r.lastHeartbeatAt)
  if (secs === null) return 'unknown'
  if (r.status === 'failed' || r.status === 'done') return 'settled'
  if (secs > 120) return 'stale-danger'
  if (secs > 30) return 'stale-warn'
  return 'fresh'
}

function formatTime(ts) {
  if (!ts) return '—'
  return new Date(ts).toLocaleString()
}

function statusClass(status) {
  return `status-${status}`
}

onMounted(() => {
  load()
  pollTimer = setInterval(load, 5000)
  tickTimer = setInterval(() => { now.value = Date.now() }, 1000)
})

onUnmounted(() => {
  clearInterval(pollTimer)
  clearInterval(tickTimer)
})
</script>

<template>
  <main class="dashboard">
    <header class="page-header">
      <h1><span class="prompt">&gt;</span> Factory Dashboard</h1>
      <div class="connection" :class="{ loading: isLoading }">
        <span class="connection-dot"></span>
        <span v-if="error">connection error</span>
        <span v-else>live</span>
      </div>
    </header>

    <section v-if="error" class="error-banner" role="alert">
      <strong>Connection lost</strong>
      <p>Failed to load runs: {{ error }}</p>
    </section>

    <section v-if="runs.length" class="run-list">
      <article
        v-for="r in runs"
        :key="r.runId"
        class="run-card"
        :class="statusClass(r.status)"
      >
        <div class="card-header">
          <div class="identity">
            <div class="repo-branch">
              <span class="repo">{{ r.repo }}</span>
              <span class="sep">/</span>
              <span class="branch mono">{{ r.branch }}</span>
            </div>
            <div class="model mono">{{ r.model }}</div>
          </div>
          <div class="status-badge" :class="statusClass(r.status)">
            <span class="status-indicator"></span>
            {{ r.status }}
          </div>
        </div>

        <div class="card-body">
          <div class="primary-stats">
            <div class="stat">
              <span class="stat-label">Last seen</span>
              <span class="stat-value mono" :class="freshnessClass(r)">{{ lastSeen(r) }}</span>
            </div>
            <div class="stat">
              <span class="stat-label">Started</span>
              <span class="stat-value mono">{{ formatTime(r.startedAt) }}</span>
            </div>
            <div v-if="r.vmName" class="stat">
              <span class="stat-label">VM</span>
              <span class="stat-value mono">{{ r.vmName }}</span>
            </div>
          </div>

          <div class="secondary">
            <div v-if="r.prUrl" class="secondary-row">
              <span class="secondary-label">PR</span>
              <a :href="r.prUrl" target="_blank" rel="noopener" class="pr-link mono">{{ r.prUrl }}</a>
            </div>
            <div v-if="r.status === 'failed' && r.failureReason" class="secondary-row failure-row">
              <span class="secondary-label">Failure</span>
              <span class="failure-reason mono">{{ r.failureReason }}</span>
            </div>
            <div v-if="r.freezeCaptured" class="secondary-row">
              <span class="secondary-label">Freeze</span>
              <span class="freeze-path mono" title="Local snapshot path">{{ r.freezeLocalPath || '—' }}</span>
            </div>
          </div>
        </div>
      </article>
    </section>

    <section v-else-if="!error" class="empty-state">
      <div class="empty-prompt">&gt;_</div>
      <h2>No runs yet</h2>
      <p>The factory is idle. New runs will appear here as they start.</p>
    </section>
  </main>
</template>

<style>
:root {
  --bg: #0b0d10;
  --surface: #11141a;
  --surface-2: #181c24;
  --border: #2a303c;
  --text: #c9d1d9;
  --text-muted: #8b949e;
  --text-dim: #6e7681;
  --accent: #3fb950;
  --accent-dim: #2ea043;
  --cyan: #39c5cf;
  --amber: #d29922;
  --red: #f85149;
  --green: #3fb950;
  --blue: #58a6ff;
  --radius: 0.75rem;
  --shadow: 0 8px 24px rgba(0, 0, 0, 0.35);
  --font-sans: system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif;
  --font-mono: ui-monospace, SFMono-Regular, "SF Mono", Menlo, Consolas, "Liberation Mono", monospace;
}

* {
  box-sizing: border-box;
}

body {
  margin: 0;
  background: var(--bg);
  color: var(--text);
  font-family: var(--font-sans);
  line-height: 1.5;
}
</style>

<style scoped>
.dashboard {
  max-width: 1100px;
  margin: 0 auto;
  padding: 2rem 1.25rem 4rem;
}

.page-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
  margin-bottom: 1.5rem;
  padding-bottom: 1rem;
  border-bottom: 1px solid var(--border);
}

h1 {
  margin: 0;
  font-size: 1.75rem;
  font-weight: 700;
  letter-spacing: -0.02em;
}

.prompt {
  color: var(--accent);
  margin-right: 0.25rem;
}

.connection {
  display: inline-flex;
  align-items: center;
  gap: 0.5rem;
  padding: 0.35rem 0.75rem;
  background: var(--surface);
  border: 1px solid var(--border);
  border-radius: 999px;
  font-size: 0.8rem;
  color: var(--text-muted);
  text-transform: uppercase;
  letter-spacing: 0.05em;
}

.connection-dot {
  width: 0.5rem;
  height: 0.5rem;
  border-radius: 50%;
  background: var(--accent);
  box-shadow: 0 0 0 0 rgba(63, 185, 80, 0.5);
  animation: pulse 2s infinite;
}

@keyframes pulse {
  0% { box-shadow: 0 0 0 0 rgba(63, 185, 80, 0.5); }
  70% { box-shadow: 0 0 0 0.4rem rgba(63, 185, 80, 0); }
  100% { box-shadow: 0 0 0 0 rgba(63, 185, 80, 0); }
}

.error-banner {
  margin-bottom: 1.5rem;
  padding: 1rem 1.25rem;
  background: rgba(248, 81, 73, 0.12);
  border: 1px solid rgba(248, 81, 73, 0.35);
  border-radius: var(--radius);
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

.run-list {
  display: grid;
  gap: 1rem;
}

.run-card {
  background: var(--surface);
  border: 1px solid var(--border);
  border-radius: var(--radius);
  box-shadow: var(--shadow);
  overflow: hidden;
  transition: border-color 0.2s ease;
}

.run-card:hover {
  border-color: var(--text-dim);
}

.card-header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 1rem;
  padding: 1rem 1.25rem;
  background: var(--surface-2);
  border-bottom: 1px solid var(--border);
}

.identity {
  min-width: 0;
}

.repo-branch {
  display: flex;
  flex-wrap: wrap;
  align-items: baseline;
  gap: 0.35rem;
  font-size: 1.1rem;
  font-weight: 600;
}

.repo {
  color: var(--text);
}

.sep {
  color: var(--text-dim);
}

.branch {
  color: var(--cyan);
}

.model {
  margin-top: 0.25rem;
  font-size: 0.85rem;
  color: var(--text-muted);
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

.status-launching { color: var(--cyan); }
.status-running { color: var(--green); }
.status-running .status-indicator { animation: blink 1.4s infinite; }
.status-stalled { color: var(--amber); }
.status-done { color: var(--blue); }
.status-failed { color: var(--red); }

@keyframes blink {
  0%, 100% { opacity: 1; }
  50% { opacity: 0.35; }
}

.card-body {
  padding: 1rem 1.25rem 1.25rem;
}

.primary-stats {
  display: flex;
  flex-wrap: wrap;
  gap: 1.25rem 2rem;
  margin-bottom: 1rem;
}

.stat {
  display: flex;
  flex-direction: column;
  gap: 0.15rem;
}

.stat-label {
  font-size: 0.7rem;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.08em;
  color: var(--text-dim);
}

.stat-value {
  font-size: 0.95rem;
  color: var(--text);
}

.stat-value.fresh { color: var(--green); }
.stat-value.stale-warn { color: var(--amber); }
.stat-value.stale-danger { color: var(--red); }
.stat-value.settled { color: var(--text-muted); }
.stat-value.unknown { color: var(--text-dim); }

.secondary {
  display: grid;
  gap: 0.5rem;
  padding-top: 1rem;
  border-top: 1px solid var(--border);
}

.secondary-row {
  display: flex;
  align-items: flex-start;
  gap: 0.75rem;
  font-size: 0.9rem;
}

.secondary-label {
  flex-shrink: 0;
  width: 4rem;
  font-size: 0.7rem;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.08em;
  color: var(--text-dim);
  padding-top: 0.15rem;
}

.pr-link {
  color: var(--blue);
  text-decoration: none;
  word-break: break-all;
}

.pr-link:hover {
  text-decoration: underline;
}

.failure-row .failure-reason {
  color: var(--red);
  word-break: break-word;
}

.freeze-path {
  color: var(--text-muted);
  word-break: break-all;
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
  margin: 0;
  max-width: 24rem;
  margin-left: auto;
  margin-right: auto;
}

.mono {
  font-family: var(--font-mono);
}

@media (max-width: 640px) {
  .page-header {
    flex-direction: column;
    align-items: flex-start;
  }

  .card-header {
    flex-direction: column;
  }

  .primary-stats {
    gap: 1rem;
  }
}
</style>
