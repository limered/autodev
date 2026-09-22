<script setup>
import { ref } from "vue";
import { PROTOTYPE_CATALOGS } from "./prototypeWorkflows.js";
import IssueRef from "../IssueRef.vue";

defineProps({ row: { type: Object, required: true } });
defineEmits(["pick"]);

const open = ref(false);

function catalogFor(row) {
  return PROTOTYPE_CATALOGS[row.catalog] ?? null;
}
function stageCount(row, name) {
  const w = catalogFor(row)?.workflows.find((x) => x.name === name);
  return w ? w.stages.length : 0;
}
</script>

<template>
  <article class="queue-row stacked">
    <div class="top">
      <span class="queue-rank mono">#{{ row.rank }}</span>
      <div class="queue-meta">
        <IssueRef :html-url="row.htmlUrl" :title="row.title" :repo="row.repo" :number="row.number" />
      </div>
      <span class="status-badge status-queued"><span class="status-indicator"></span>queued</span>
      <button class="remove-button" title="stub — no-op in prototype">Remove</button>
    </div>
    <div class="footer">
      <div class="menu-wrap">
        <button class="trigger" @click="open = !open" @keyup.escape="open = false" aria-haspopup="listbox" :aria-expanded="open">
          workflow: {{ row.picked }} <span class="muted">· {{ stageCount(row, row.picked) }} stages</span> <span class="chev">▾</span>
        </button>
        <ul v-if="open" class="menu" role="listbox">
          <li v-for="w in catalogFor(row).workflows" :key="w.name" role="option" :aria-selected="w.name === row.picked">
            <button :class="{ active: w.name === row.picked }" @click="$emit('pick', w.name); open = false">
              <span class="check">{{ w.name === row.picked ? "✓" : "" }}</span>
              <span class="name">{{ w.name }}</span>
              <span class="count">{{ w.stages.length }}</span>
            </button>
          </li>
        </ul>
      </div>
    </div>
  </article>
</template>

<style scoped>
.queue-row { min-width: 0; padding: 0.75rem 1rem; background: var(--surface); border: 1px solid var(--border); border-radius: var(--radius); }
.stacked { display: flex; flex-direction: column; gap: 0; }
.top { display: flex; align-items: center; min-width: 0; gap: 0.75rem; }
.queue-rank { flex-shrink: 0; width: 2rem; font-size: 0.85rem; color: var(--text-dim); }
.mono { font-family: var(--font-mono); }
.queue-meta { display: flex; flex-direction: column; min-width: 0; flex: 1; gap: 0.15rem; }
.footer { margin-top: 0.6rem; padding-top: 0.5rem; border-top: 1px solid var(--border); }
.menu-wrap { position: relative; display: inline-block; }
.trigger { background: none; border: none; padding: 0; font-size: 0.85rem; color: var(--text); cursor: pointer; }
.trigger:hover { text-decoration: underline; }
.muted { color: var(--text-muted); font-size: 0.8rem; }
.chev { color: var(--text-muted); font-size: 0.75em; }
.menu { position: absolute; top: 100%; left: 0; z-index: 50; list-style: none; margin: 0.25rem 0 0; padding: 0.25rem 0; background: var(--surface); border: 1px solid var(--border); border-radius: var(--radius); min-width: 13rem; box-shadow: 0 8px 24px rgba(0, 0, 0, 0.35); }
.menu button { display: flex; gap: 0.5rem; align-items: baseline; width: 100%; background: none; border: none; padding: 0.4rem 0.75rem; font-size: 0.85rem; color: var(--text); cursor: pointer; text-align: left; }
.menu button:hover { background: rgba(127, 127, 127, 0.12); }
.menu button.active { font-weight: 700; }
.check { width: 1em; color: var(--accent); }
.name { flex: 1; }
.count { color: var(--text-muted); font-size: 0.78rem; }
.status-badge { display: inline-flex; align-items: center; gap: 0.4rem; flex-shrink: 0; padding: 0.35rem 0.7rem; border-radius: 999px; font-size: 0.8rem; font-weight: 600; text-transform: uppercase; letter-spacing: 0.04em; background: var(--surface); border: 1px solid currentColor; }
.status-indicator { width: 0.5rem; height: 0.5rem; border-radius: 50%; background: currentColor; }
.status-queued { color: var(--text-muted); }
.remove-button { flex-shrink: 0; padding: 0.3rem 0.6rem; background: transparent; border: 1px solid var(--border); border-radius: var(--radius); color: var(--text-muted); font-size: 0.75rem; font-weight: 600; cursor: pointer; }
</style>
