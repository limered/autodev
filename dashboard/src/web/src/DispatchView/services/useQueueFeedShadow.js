import { ref, watch } from "vue";
import { useDragReorder } from "./useDragReorder.js";

export function useQueueFeedShadow({ feed, isSaving, onReorder }) {
  const localQueue = ref([]);
  const drag = useDragReorder({ items: localQueue, onReorder });

  watch(
    feed,
    (newQueue) => {
      drag.syncFeed(newQueue ?? [], { isSaving: isSaving.value });
    },
    { immediate: true },
  );

  return { localQueue, ...drag };
}
