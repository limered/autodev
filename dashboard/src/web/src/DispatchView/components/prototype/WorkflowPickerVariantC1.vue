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
  <div class="proto-c1">
    <template v-if="row.runId">
      <span class="plain">workflow: {{ row.picked }} <span class="muted">(frozen)</span></span>
    </template>
    <template v-else-if="!catalogFor(row)">
      <span class="muted"><i>workflow: default (no catalog)</i></span>
    </template>
    <template v-else>
      <div class="menu-wrap">
        <button class="trigger" @click="open = !open" @keyup.escape="open = false" aria-haspopup="listbox" :aria-expanded="open">
          workflow: {{ row.picked }} <span class="chev">▾</span>
        </button>
        <ul v-if="open" class="menu" role="listbox">
          <li v-for="w in catalogFor(row).workflows" :key="w.name" role="option" :aria-selected="w.name === row.picked">
            <button :class="{ active: w.name === row.picked }" @click="$emit('pick', w.name); open = false">
              <span class="check">{{ w.name === row.picked ? "✓" : "" }}</span>{{ w.name }}
            </button>
          </li>
        </ul>
        <p v-if="!catalogFor(row).workflows.some((w) => w.name === row.picked)" class="vanished">
          “{{ row.picked }}” vanished —
          <button class="link" @click="$emit('pick', catalogFor(row).defaultWorkflow)">use {{ catalogFor(row).defaultWorkflow }}</button>
        </p>
      </div>
    </template>
  </div>
</template>

<style scoped>
.proto-c1 { margin-top: 0.3rem; font-size: 0.85rem; }
.plain { color: var(--text); }
.muted { color: var(--text-muted); font-size: 0.8rem; }
.menu-wrap { position: relative; display: inline-block; }
.trigger { background: none; border: none; padding: 0; font-size: inherit; color: var(--text); cursor: pointer; }
.trigger:hover { text-decoration: underline; }
.chev { color: var(--text-muted); font-size: 0.75em; }
.menu {
  position: absolute; top: 100%; left: 0; z-index: 50;
  list-style: none; margin: 0.25rem 0 0; padding: 0.25rem 0;
  background: var(--surface); border: 1px solid var(--border); border-radius: var(--radius);
  min-width: 12rem; box-shadow: 0 8px 24px rgba(0, 0, 0, 0.35);
}
.menu button { display: flex; gap: 0.5rem; align-items: baseline; width: 100%; background: none; border: none; padding: 0.4rem 0.75rem; font-size: 0.85rem; color: var(--text); cursor: pointer; text-align: left; }
.menu button:hover { background: rgba(127, 127, 127, 0.12); }
.menu button.active { font-weight: 700; }
.check { width: 1em; color: var(--accent); }
.vanished { color: var(--red); font-size: 0.8rem; }
.link { background: none; border: none; padding: 0; color: var(--accent); cursor: pointer; text-decoration: underline; }
</style>
