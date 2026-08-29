import { describe, it, expect, vi } from 'vitest'
import { ref } from 'vue'
import { reorderQueue, useDragReorder } from '../../services/useDragReorder.js'

function fakeDragEvent() {
  return { dataTransfer: { effectAllowed: null, setData: vi.fn() } }
}

describe('reorderQueue', () => {
  it('places a downward drag at the visually-indicated drop target', () => {
    const list = ['a', 'b', 'c', 'd', 'e']
    // drag 'b' (index 1) down onto 'd' (index 3). After splicing 'b' out, 'd'
    // shifts to index 2, so the drop slot is toIndex - 1 = 2, not the stale 3.
    // (The buggy insert-at-toIndex would yield ['a','c','d','b','e'].)
    expect(reorderQueue(list, 1, 3)).toEqual(['a', 'c', 'b', 'd', 'e'])
  })

  it('places an upward drag at the visually-indicated drop target', () => {
    const list = ['a', 'b', 'c', 'd', 'e']
    // drag 'd' (index 3) up onto 'b' (index 1): removal does not shift the
    // target, so insert at toIndex = 1.
    expect(reorderQueue(list, 3, 1)).toEqual(['a', 'd', 'b', 'c', 'e'])
  })

  it('returns the same reference when source and target are the same slot', () => {
    const list = ['a', 'b', 'c']
    expect(reorderQueue(list, 1, 1)).toBe(list)
  })

  it('returns the same reference for out-of-range indices', () => {
    const list = ['a', 'b', 'c']
    expect(reorderQueue(list, -1, 2)).toBe(list)
    expect(reorderQueue(list, 0, 99)).toBe(list)
  })

  it('does not mutate the input list', () => {
    const list = ['a', 'b', 'c', 'd']
    reorderQueue(list, 1, 3)
    expect(list).toEqual(['a', 'b', 'c', 'd'])
  })
})

describe('useDragReorder', () => {
  it('reorders a downward drop and persists the new id order', async () => {
    const items = ref([
      { id: 'q1' }, { id: 'q2' }, { id: 'q3' }, { id: 'q4' }
    ])
    const onReorder = vi.fn(() => Promise.resolve())
    const drag = useDragReorder({ items, onReorder })

    drag.onDragStart(items.value[1], fakeDragEvent()) // grab q2
    await drag.onDrop('q4') // drop onto q4 (downward)

    expect(items.value.map(i => i.id)).toEqual(['q1', 'q3', 'q2', 'q4'])
    expect(onReorder).toHaveBeenCalledTimes(1)
    expect(onReorder).toHaveBeenCalledWith(['q1', 'q3', 'q2', 'q4'])
  })

  it('reorders an upward drop and persists the new id order', async () => {
    const items = ref([
      { id: 'q1' }, { id: 'q2' }, { id: 'q3' }, { id: 'q4' }
    ])
    const onReorder = vi.fn(() => Promise.resolve())
    const drag = useDragReorder({ items, onReorder })

    drag.onDragStart(items.value[3], fakeDragEvent()) // grab q4
    await drag.onDrop('q1') // drop onto q1 (upward)

    expect(items.value.map(i => i.id)).toEqual(['q4', 'q1', 'q2', 'q3'])
    expect(onReorder).toHaveBeenCalledTimes(1)
    expect(onReorder).toHaveBeenCalledWith(['q4', 'q1', 'q2', 'q3'])
  })

  it('does not persist when the drop target is the dragged item', async () => {
    const items = ref([{ id: 'q1' }, { id: 'q2' }])
    const onReorder = vi.fn(() => Promise.resolve())
    const drag = useDragReorder({ items, onReorder })

    drag.onDragStart(items.value[0], fakeDragEvent())
    await drag.onDrop('q1')

    expect(items.value.map(i => i.id)).toEqual(['q1', 'q2'])
    expect(onReorder).not.toHaveBeenCalled()
  })

  it('clears the drag-over highlight and dragged id on drop', async () => {
    const items = ref([{ id: 'q1' }, { id: 'q2' }, { id: 'q3' }])
    const onReorder = vi.fn(() => Promise.resolve())
    const drag = useDragReorder({ items, onReorder })

    drag.onDragStart(items.value[0], fakeDragEvent())
    drag.onDragOver({ preventDefault: () => {} }, 'q3')
    expect(drag.dragOverId.value).toBe('q3')
    expect(drag.draggedId.value).toBe('q1')

    await drag.onDrop('q3')

    expect(drag.dragOverId.value).toBeNull()
    expect(drag.draggedId.value).toBeNull()
  })
})
