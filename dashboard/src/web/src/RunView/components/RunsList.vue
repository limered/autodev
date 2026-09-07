<script setup>
// Thin container for the runs page split (issue #52): the small, polling
// active list on top and the paginated, non-polling history below — each is
// its own component under RunView/components/.
//
// The split is client-side over the existing endpoints (no new API surface):
// /runs/active already returns the whole active set in one fetch, and history
// keeps windowing /runs with skip/take, filtering to terminal runs for display
// only.
//
// Coordination is deliberately one-directional: when the active set changes (a
// run started or finished) history's first page is refreshed once so the run
// appears/updates there. Active polling otherwise never touches history, which
// keeps the offset paging stable (issue #46).
import { ref } from "vue";
import ActiveRunsList from "./ActiveRunsList.vue";
import RunsHistoryList from "./RunsHistoryList.vue";
import PrototypeDevLoopHost from "./PrototypeDevLoopHost.vue";

const history = ref(null);

function onActiveSetChange() {
  history.value?.refreshFirst();
}
</script>

<template>
  <div class="runs-page">
    <ActiveRunsList @set-change="onActiveSetChange" />
    <RunsHistoryList ref="history" />
    <PrototypeDevLoopHost />
  </div>
</template>

<style scoped>
.runs-page {
  display: flex;
  flex-direction: column;
}
</style>
