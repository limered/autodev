<script setup>
import { ref } from "vue";
import { useRoute } from "vue-router";
import WorkflowPickerVariantC2 from "./WorkflowPickerVariantC2.vue";
import WorkflowPickerVariantD1 from "./WorkflowPickerVariantD1.vue";
import WorkflowPickerVariantD2 from "./WorkflowPickerVariantD2.vue";
import WorkflowPickerVariantD3 from "./WorkflowPickerVariantD3.vue";
import PrototypeSwitcher from "./PrototypeSwitcher.vue";

const route = useRoute();
const row = ref({
  id: "q1",
  rank: 1,
  title: "Add login page",
  repo: "acme/web",
  number: 101,
  htmlUrl: "https://example.com/acme/web/101",
  runId: null,
  picked: "full",
  catalog: "normal",
});

function onPick(name) {
  row.value.picked = name;
}

const variants = [
  { key: "C2", label: "C2 baseline (fragment)" },
  { key: "D1", label: "full card, picker under title" },
  { key: "D2", label: "full card, picker inline right" },
  { key: "D3", label: "full card, picker in footer" },
];
const variant = () => route.query.variant ?? "D1";
</script>

<template>
  <section class="prototype-panel">
    <p class="prototype-note">
      PROTOTYPE for <a href="https://github.com/limered/autodev/issues/234">#234</a> — throwaway, in-memory only.
      Single #1 state (unclaimed, normal catalog). Remove is a no-op stub.
      State after every pick: {{ `${row.id}=${row.picked}` }}
    </p>
    <WorkflowPickerVariantC2 v-if="variant() === 'C2'" :row="row" @pick="onPick" />
    <WorkflowPickerVariantD1 v-else-if="variant() === 'D1'" :row="row" @pick="onPick" />
    <WorkflowPickerVariantD2 v-else-if="variant() === 'D2'" :row="row" @pick="onPick" />
    <WorkflowPickerVariantD3 v-else :row="row" @pick="onPick" />
    <PrototypeSwitcher :variants="variants" />
  </section>
</template>

<style scoped>
.prototype-panel { margin-top: 1.5rem; border: 2px dashed #ff0; padding: 1rem; display: flex; flex-direction: column; gap: 0.75rem; }
.prototype-note { font-size: 0.8rem; margin: 0; }
</style>
