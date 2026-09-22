<script setup>
import { ref } from "vue";
import { PROTOTYPE_CATALOGS } from "./prototypeWorkflows.js";

defineProps({ row: { type: Object, required: true } });
defineEmits(["pick"]);

const open = ref(false);

function catalogFor(row) {
  return PROTOTYPE_CATALOGS[row.catalog] ?? null;
}
</script>

<template>
  <div class="proto-c3">
    <template v-if="row.runId">
      <span class="plain">workflow: {{ row.picked }} <span class="muted">(frozen)</span></span>
    </template>
    <template v-else-if="!catalogFor(row)">
      <span class="muted"><i>workflow: default (no catalog)</i></span>
    </template>
    <template v-else>
      <button class="trigger" @click="open = !open" :aria-expanded="open">
        workflow: {{ row.picked }} <span class="chev">{{ open ? "▴" : "▾" }}</span>
      </button>
      <ul v-if="open" class="inline-list">
        <li v-for="w in catalogFor(row).workflows" :key="w.name">
          <button :class="{ active: w.name === row.picked }" @click="$emit('pick', w.name)">
            <span class="check">{{ w.name === row.picked ? "✓" : "" }}</span>{{ w.name }}
          </button>
        </li>
      </ul>
      <p v-if="!catalogFor(row).workflows.some((w) => w.name === row.picked)" class="vanished">
        “{{ row.picked }}” vanished —
        <button class="link" @click="$emit('pick', catalogFor(row).defaultWorkflow)">use {{ catalogFor(row).defaultWorkflow }}</button>
      </p>
    </template>
  </div>
</template>

<style scoped>
.proto-c3 { margin-top: 0.3rem; font-size: 0.85rem; }
.plain { color: var(--text); }
.muted { color: var(--text-muted); font-size: 0.8rem; }
.trigger { display: block; background: none; border: none; padding: 0; font-size: inherit; color: var(--text); cursor: pointer; }
.trigger:hover { text-decoration: underline; }
.chev { color: var(--text-muted); font-size: 0.75em; }
.inline-list { list-style: none; margin: 0.25rem 0 0; padding: 0 0 0 0.25rem; border-left: 2px solid var(--border); }
.inline-list button { display: flex; gap: 0.5rem; align-items: baseline; background: none; border: none; padding: 0.25rem 0.5rem; font-size: 0.85rem; color: var(--text); cursor: pointer; text-align: left; }
.inline-list button:hover { text-decoration: underline; }
.inline-list button.active { font-weight: 700; }
.check { width: 1em; color: var(--accent); }
.vanished { color: var(--red); font-size: 0.8rem; }
.link { background: none; border: none; padding: 0; color: var(--accent); cursor: pointer; text-decoration: underline; }
</style>
