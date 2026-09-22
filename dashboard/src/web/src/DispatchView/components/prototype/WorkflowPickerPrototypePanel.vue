<script setup>
import { ref } from "vue";
import { useRoute } from "vue-router";
import { PROTOTYPE_ROWS } from "./prototypeWorkflows.js";
import WorkflowPickerVariantA from "./WorkflowPickerVariantA.vue";
import WorkflowPickerVariantB from "./WorkflowPickerVariantB.vue";
import WorkflowPickerVariantC1 from "./WorkflowPickerVariantC1.vue";
import WorkflowPickerVariantC2 from "./WorkflowPickerVariantC2.vue";
import WorkflowPickerVariantC3 from "./WorkflowPickerVariantC3.vue";
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
  { key: "C1", label: "text + floating name-only menu" },
  { key: "C2", label: "text + count floating menu" },
  { key: "C3", label: "text inline expanding list" },
];
const variant = () => route.query.variant ?? "C1";
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
      <WorkflowPickerVariantC1 v-else-if="variant() === 'C1'" :row="row" @pick="(n) => onPick(row.id, n)" />
      <WorkflowPickerVariantC2 v-else-if="variant() === 'C2'" :row="row" @pick="(n) => onPick(row.id, n)" />
      <WorkflowPickerVariantC3 v-else :row="row" @pick="(n) => onPick(row.id, n)" />
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
