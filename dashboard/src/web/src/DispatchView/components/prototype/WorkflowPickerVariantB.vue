<script setup>
import { PROTOTYPE_CATALOGS } from "./prototypeWorkflows.js";

defineProps({ row: { type: Object, required: true } });
defineEmits(["pick"]);

function catalogFor(row) {
  return PROTOTYPE_CATALOGS[row.catalog] ?? null;
}
</script>

<template>
  <div class="proto-b">
    <div class="second-line">
      <template v-if="row.runId">
        <span class="frozen-chip">{{ row.picked }}</span>
        <span class="hint">frozen at claim — stage lights seeded from this workflow</span>
      </template>
      <template v-else-if="!catalogFor(row)">
        <span class="hint">no catalog — runs factory default; picker disabled</span>
      </template>
      <template v-else>
        <button
          v-for="w in catalogFor(row).workflows"
          :key="w.name"
          class="chip"
          :class="{ active: w.name === row.picked }"
          :disabled="!!row.runId"
          :title="w.stages.join(' → ')"
          @click="$emit('pick', w.name)"
        >
          {{ w.name }} · {{ w.stages.length }}
        </button>
        <span v-if="!catalogFor(row).workflows.some((w) => w.name === row.picked)" class="vanished">
          “{{ row.picked }}” no longer in catalog —
          <button @click="$emit('pick', catalogFor(row).defaultWorkflow)">use {{ catalogFor(row).defaultWorkflow }}</button>
        </span>
      </template>
    </div>
  </div>
</template>

<style scoped>
.proto-b .second-line { display: flex; flex-wrap: wrap; gap: 0.4rem; align-items: center; margin-top: 0.35rem; }
.chip { border: 1px solid var(--border); background: var(--surface); border-radius: 999px; padding: 0.2rem 0.7rem; font-size: 0.78rem; cursor: pointer; }
.chip.active { border-color: var(--accent); color: var(--accent); }
.frozen-chip { background: var(--surface); border: 1px solid var(--accent); color: var(--accent); border-radius: 999px; padding: 0.2rem 0.7rem; font-size: 0.78rem; font-weight: 700; }
.hint { font-size: 0.75rem; color: var(--text-muted); }
.vanished { font-size: 0.78rem; color: var(--red); }
</style>
