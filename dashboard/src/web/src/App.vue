<script setup>
import { ref, onMounted, onUnmounted } from 'vue'

const runs = ref([])
const error = ref(null)
const now = ref(Date.now())
let pollTimer = null
let tickTimer = null

async function load() {
  try {
    const res = await fetch('/runs')
    if (!res.ok) throw new Error(`HTTP ${res.status}`)
    runs.value = await res.json()
    error.value = null
  } catch (e) {
    error.value = e.message
  }
}

function lastSeen(r) {
  if (!r.lastHeartbeatAt) return '—'
  const secs = Math.max(0, Math.round((now.value - new Date(r.lastHeartbeatAt).getTime()) / 1000))
  if (secs < 60) return `${secs}s ago`
  if (secs < 3600) return `${Math.floor(secs / 60)}m ago`
  return `${Math.floor(secs / 3600)}h ago`
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
  <main>
    <h1>Factory Dashboard</h1>
    <p v-if="error" class="error">Failed to load: {{ error }}</p>
    <table v-if="runs.length">
      <thead>
        <tr><th>Repo</th><th>Branch</th><th>Model</th><th>Status</th><th>Last seen</th><th>PR</th><th>Failure</th><th>Freeze</th></tr>
      </thead>
      <tbody>
        <tr v-for="r in runs" :key="r.runId">
          <td>{{ r.repo }}</td>
          <td>{{ r.branch }}</td>
          <td>{{ r.model }}</td>
          <td>{{ r.status }}</td>
          <td>{{ lastSeen(r) }}</td>
          <td><a v-if="r.prUrl" :href="r.prUrl" target="_blank" rel="noopener">PR</a><span v-else>—</span></td>
          <td>{{ r.status === 'failed' ? (r.failureReason || '—') : '' }}</td>
          <td><span v-if="r.freezeCaptured" class="freeze">freeze: {{ r.freezeLocalPath || '—' }}</span><span v-else>—</span></td>
        </tr>
      </tbody>
    </table>
    <p v-else-if="!error">No runs yet.</p>
  </main>
</template>

<style>
body { font-family: system-ui, sans-serif; margin: 2rem; }
table { border-collapse: collapse; margin-top: 1rem; }
th, td { border: 1px solid #ccc; padding: 0.4rem 0.8rem; text-align: left; }
th { background: #f3f3f3; }
.error { color: #b00; }
.freeze { color: #666; font-family: ui-monospace, monospace; font-size: 0.85em; }
</style>
