<script setup>
import { ref } from "vue";
import { useRoute } from "vue-router";
import { PROTOTYPE_ROWS } from "./prototypeWorkflows.js";
import WorkflowPickerVariantA from "./WorkflowPickerVariantA.vue";
import WorkflowPickerVariantB from "./WorkflowPickerVariantB.vue";
import WorkflowPickerVariantC from "./WorkflowPickerVariantC.vue";
import PrototypeSwitcher from "./PrototypeSwitcher.vue";

const route = useRoute();
const rows = ref(PROTOTYPE_ROWS.map((r) => ({ ...r })));

function onPick(id, name) {
  const row = rows.value.find((r) => r.id === id);
  if (row && !row.runId) row.picked = name;
}

const variants = [
  { key: "A", label: "inline dropdown" },
  { key: "B", label: "chip row + stage counts" },
  { key: "C", label: "compact badge popover" },
];
const variant = () => route.query.variant ?? "A";
</script>

<template>
  <section class="prototype-panel">
    <p class="prototype-note">
      PROTOTYPE for <a href="https://github.com/limered/autodev/issues/234">#234</a> — throwaway, in-memory only.
      Rows cover: editable unclaimed, frozen claimed, missing catalog, vanished pick.
      State after every action: {{ rows.map((r) => `${r.id}=${r.picked}${r.runId ? "(frozen)" : ""}`).join(", ") }}
    </p>
    <article v-for="row in rows" :key="row.id" class="prototype-row">
      <header>#{{ row.rank }} {{ row.title }} ({{ row.repo }}#{{ row.number }}){{ row.runId ? " · claimed" : "" }}</header>
      <WorkflowPickerVariantA v-if="variant() === 'A'" :row="row" @pick="(n) => onPick(row.id, n)" />
      <WorkflowPickerVariantB v-else-if="variant() === 'B'" :row="row" @pick="(n) => onPick(row.id, n)" />
      <WorkflowPickerVariantC v-else :row="row" @pick="(n) => onPick(row.id, n)" />
    </article>
    <PrototypeSwitcher :variants="variants" />
  </section>
</template>

<style scoped>
.prototype-panel { margin-top: 1.5rem; border: 2px dashed #ff0; padding: 1rem; }
.prototype-note { font-size: 0.8rem; }
.prototype-row { border-top: 1px solid var(--border); padding: 0.6rem 0; }
.prototype-row header { font-size: 0.85rem; font-weight: 600; }
</style>
