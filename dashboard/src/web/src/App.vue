<script setup>
import { ref, onMounted, onUnmounted } from 'vue'

const runs = ref([])
const error = ref(null)
let timer = null

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

onMounted(() => {
  load()
  timer = setInterval(load, 5000)
})
onUnmounted(() => clearInterval(timer))
</script>

<template>
  <main>
    <h1>Factory Dashboard</h1>
    <p v-if="error" class="error">Failed to load: {{ error }}</p>
    <table v-if="runs.length">
      <thead>
        <tr><th>Repo</th><th>Branch</th><th>Model</th><th>Status</th></tr>
      </thead>
      <tbody>
        <tr v-for="r in runs" :key="r.runId">
          <td>{{ r.repo }}</td>
          <td>{{ r.branch }}</td>
          <td>{{ r.model }}</td>
          <td>{{ r.status }}</td>
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
</style>
