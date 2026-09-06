import { ref } from "vue";

// Pure reorder core, internal to the drop below: move the item at `fromIndex`
// into the slot visually indicated by `toIndex`. When dragging downward
// (fromIndex < toIndex) the source is removed before the insert, so every
// later slot shifts down by one and the original `toIndex` now points one
// past the drop target — re-insert at `toIndex - 1` so the item lands on the
// slot the user saw. Upward drags keep `toIndex` as-is. Returns the same array
// reference when no reorder is needed, letting the drop cheaply detect a no-op.
function moveItem(list, fromIndex, toIndex) {
  if (!Array.isArray(list)) return list;
  if (fromIndex < 0 || toIndex < 0) return list;
  if (fromIndex >= list.length || toIndex >= list.length) return list;
  if (fromIndex === toIndex) return list;

  const reordered = [...list];
  const [moved] = reordered.splice(fromIndex, 1);
  const insertAt = fromIndex < toIndex ? toIndex - 1 : toIndex;
  reordered.splice(insertAt, 0, moved);
  return reordered;
}

// Drag-to-reorder seam for the run queue: owns the whole drop — the transient
// drag state, the optimistic move, the persist (`onReorder`, which persists
// and re-syncs), and the shadow-copy convergence (`syncFeed`, called from the
// column's feed watch). The caller hands in the reactive shadow list plus the
// persist step; the composable never knows about fetch, so the math stays
// testable with no network.
export function useDragReorder({ items, onReorder }) {
  const draggedId = ref(null);
  const dragOverId = ref(null);

  function onDragStart(item, event) {
    draggedId.value = item.id;
    event.dataTransfer.effectAllowed = "move";
    event.dataTransfer.setData("text/plain", String(item.id));
  }

  function onDragOver(event, id) {
    event.preventDefault();
    dragOverId.value = id;
  }

  function onDragLeave() {
    dragOverId.value = null;
  }

  function onDragEnd() {
    draggedId.value = null;
    dragOverId.value = null;
  }

  // Single drop orchestrating move → persist → reconverge: the rows move
  // first for instant feedback, `onReorder` persists and re-syncs the feed,
  // and the next feed tick reconverges the shadow via syncFeed below.
  async function onDrop(targetId) {
    dragOverId.value = null;
    const sourceId = draggedId.value;
    draggedId.value = null;

    if (sourceId === null || sourceId === targetId) return;

    const fromIndex = items.value.findIndex((i) => i.id === sourceId);
    const toIndex = items.value.findIndex((i) => i.id === targetId);
    const reordered = moveItem(items.value, fromIndex, toIndex);
    if (reordered === items.value) return;
    items.value = reordered;

    await onReorder(reordered.map((i) => i.id));
  }

  // Convergence policy for the shadow copy (issue #75): while idle the feed
  // is the truth — take it, copied so the shadow never aliases the feed
  // array — but while a save is in flight or a drag is active the local order
  // wins, so a polling sync never yanks rows out from under an in-progress
  // interaction.
  function syncFeed(feedQueue, { isSaving = false } = {}) {
    if (isSaving || draggedId.value !== null) return;
    items.value = [...feedQueue];
  }

  return {
    draggedId,
    dragOverId,
    onDragStart,
    onDragOver,
    onDragLeave,
    onDragEnd,
    onDrop,
    syncFeed,
  };
}
