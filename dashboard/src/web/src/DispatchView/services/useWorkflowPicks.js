import { ref } from "vue";

export function useWorkflowPicks() {
  const pickedById = ref(new Map());

  function activePick(queueId, defaultName) {
    return pickedById.value.get(queueId) ?? defaultName;
  }

  function setPick(queueId, name) {
    pickedById.value.set(queueId, name);
  }

  return { activePick, setPick };
}
