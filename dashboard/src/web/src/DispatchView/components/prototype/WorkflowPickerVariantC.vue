<script setup>
import { PROTOTYPE_CATALOGS } from "./prototypeWorkflows.js";

defineProps({ row: { type: Object, required: true } });
defineEmits(["pick"]);

function catalogFor(row) {
  return PROTOTYPE_CATALOGS[row.catalog] ?? null;
}
</script>

<template>
  <div class="proto-c">
    <details v-if="!row.runId && catalogFor(row)" class="picker-details">
      <summary class="badge">workflow: {{ row.picked }} ▾</summary>
      <ul>
        <li v-for="w in catalogFor(row).workflows" :key="w.name">
          <button :class="{ active: w.name === row.picked }" @click="$emit('pick', w.name)">
            {{ w.name }}
          </button>
          <span class="stages">{{ w.stages.join(" → ") }}</span>
        </li>
      </ul>
      <p v-if="!catalogFor(row).workflows.some((w) => w.name === row.picked)" class="vanished">
        Saved pick “{{ row.picked }}” vanished — choose again; claim validates and fails stale picks.
      </p>
    </details>
    <span v-else-if="row.runId" class="frozen" title="frozen into claim payload at start">🔒 {{ row.picked }}</span>
    <span v-else class="fallback" title="factory fallback">workflow: default (no catalog)</span>
  </div>
</template>

<style scoped>
.proto-c { margin-top: 0.3rem; font-size: 0.8rem; }
.badge { cursor: pointer; border: 1px solid var(--border); border-radius: 999px; padding: 0.15rem 0.6rem; display: inline-block; }
.frozen { border: 1px solid var(--accent); border-radius: 999px; padding: 0.15rem 0.6rem; color: var(--accent); }
.fallback { color: var(--text-muted); font-style: italic; }
.picker-details ul { list-style: none; margin: 0.4rem 0; padding: 0; }
.picker-details li { display: flex; gap: 0.5rem; align-items: baseline; margin-bottom: 0.25rem; }
.stages { color: var(--text-muted); font-size: 0.75rem; }
.vanished { color: var(--red); }
button.active { font-weight: 700; }
</style>
