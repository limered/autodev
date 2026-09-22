<script setup>
import { PROTOTYPE_CATALOGS } from "./prototypeWorkflows.js";

defineProps({ row: { type: Object, required: true } });
defineEmits(["pick"]);

function catalogFor(row) {
  return PROTOTYPE_CATALOGS[row.catalog] ?? null;
}
</script>

<template>
  <div class="proto-a">
    <template v-if="row.runId">
      <span class="frozen">workflow: {{ row.picked }} · frozen</span>
    </template>
    <template v-else-if="!catalogFor(row)">
      <span class="fallback">workflow: default (catalog missing — factory fallback)</span>
    </template>
    <template v-else-if="!catalogFor(row).workflows.some((w) => w.name === row.picked)">
      <span class="vanished">picked “{{ row.picked }}” vanished — <button @click="$emit('pick', 'full')">reset to default</button></span>
    </template>
    <label v-else class="inline-pick">
      workflow:
      <select :value="row.picked" :disabled="!!row.runId" @change="$emit('pick', $event.target.value)">
        <option v-for="w in catalogFor(row).workflows" :key="w.name" :value="w.name">
          {{ w.name }} ({{ w.stages.length }} stages)
        </option>
      </select>
    </label>
  </div>
</template>

<style scoped>
.proto-a { margin-top: 0.3rem; font-size: 0.8rem; color: var(--text-muted); }
.frozen { border: 1px solid var(--border); border-radius: 999px; padding: 0.15rem 0.6rem; }
.fallback, .vanished { color: var(--text-muted); font-style: italic; }
.inline-pick select { margin-left: 0.3rem; }
</style>
