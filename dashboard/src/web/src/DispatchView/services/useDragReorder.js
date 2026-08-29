import { ref } from 'vue'

// Pure reorder core: move the item at `fromIndex` into the slot visually
// indicated by `toIndex`. When dragging downward (fromIndex < toIndex) the
// source is removed before the insert, so every later slot shifts down by one
// and the original `toIndex` now points one past the drop target — re-insert at
// `toIndex - 1` so the item lands on the slot the user saw. Upward drags keep
// `toIndex` as-is. Returns the same array reference when no reorder is needed,
// letting callers cheaply detect a no-op.
export function reorderQueue(list, fromIndex, toIndex) {
  if (!Array.isArray(list)) return list
  if (fromIndex < 0 || toIndex < 0) return list
  if (fromIndex >= list.length || toIndex >= list.length) return list
  if (fromIndex === toIndex) return list

  const reordered = [...list]
  const [moved] = reordered.splice(fromIndex, 1)
  const insertAt = fromIndex < toIndex ? toIndex - 1 : toIndex
  reordered.splice(insertAt, 0, moved)
  return reordered
}

// Drag-to-reorder seam for the run queue: owns the transient drag state and the
// index bookkeeping, leaving the component free to wire handlers to the rows and
// inject the persist step (`onReorder`). The caller hands in the reactive list to
// reorder and a callback that receives the new id order; the composable never
// knows about fetch or the queue action, so the reorder math is testable in
// isolation from the network.
export function useDragReorder({ items, onReorder }) {
  const draggedId = ref(null)
  const dragOverId = ref(null)

  function onDragStart(item, event) {
    draggedId.value = item.id
    event.dataTransfer.effectAllowed = 'move'
    event.dataTransfer.setData('text/plain', String(item.id))
  }

  function onDragOver(event, id) {
    event.preventDefault()
    dragOverId.value = id
  }

  function onDragLeave() {
    dragOverId.value = null
  }

  function onDragEnd() {
    draggedId.value = null
    dragOverId.value = null
  }

  async function onDrop(targetId) {
    dragOverId.value = null
    const sourceId = draggedId.value
    draggedId.value = null

    if (sourceId === null || sourceId === targetId) return

    const fromIndex = items.value.findIndex(i => i.id === sourceId)
    const toIndex = items.value.findIndex(i => i.id === targetId)
    const reordered = reorderQueue(items.value, fromIndex, toIndex)
    if (reordered === items.value) return
    items.value = reordered

    await onReorder(reordered.map(i => i.id))
  }

  return {
    draggedId,
    dragOverId,
    onDragStart,
    onDragOver,
    onDragLeave,
    onDragEnd,
    onDrop
  }
}
