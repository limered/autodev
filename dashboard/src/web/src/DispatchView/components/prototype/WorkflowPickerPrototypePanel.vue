<script setup>
import { ref } from "vue";
import WorkflowPickerVariantD2 from "./WorkflowPickerVariantD2.vue";

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
</script>

<template>
  <section class="prototype-panel">
    <p class="prototype-note">
      PROTOTYPE for <a href="https://github.com/limered/autodev/issues/234">#234</a> — throwaway, in-memory only.
      Winner D2: full card, picker inline right. Single #1 state (unclaimed, normal catalog).
      Remove is a no-op stub. State after every pick: {{ `${row.id}=${row.picked}` }}
    </p>
    <WorkflowPickerVariantD2 :row="row" @pick="onPick" />
  </section>
</template>

<style scoped>
.prototype-panel { margin-top: 1.5rem; border: 2px dashed #ff0; padding: 1rem; display: flex; flex-direction: column; gap: 0.75rem; }
.prototype-note { font-size: 0.8rem; margin: 0; }
</style>
