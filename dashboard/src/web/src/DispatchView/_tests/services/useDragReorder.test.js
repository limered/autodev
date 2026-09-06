import { describe, it, expect, vi } from "vitest";
import { ref } from "vue";
import { useDragReorder } from "../../services/useDragReorder.js";

function fakeDragEvent() {
  return { dataTransfer: { effectAllowed: null, setData: vi.fn() } };
}

describe("useDragReorder", () => {
  it("reorders a downward drop and persists the new id order", async () => {
    const items = ref([{ id: "q1" }, { id: "q2" }, { id: "q3" }, { id: "q4" }]);
    const onReorder = vi.fn(() => Promise.resolve());
    const drag = useDragReorder({ items, onReorder });

    drag.onDragStart(items.value[1], fakeDragEvent()); // grab q2
    await drag.onDrop("q4"); // drop onto q4 (downward)

    // After splicing q2 out, q4 shifts down one slot, so q2 lands just
    // before q4 — the slot the user saw. (Insert-at-stale-toIndex would
    // yield ["q1","q3","q4","q2"].)
    expect(items.value.map((i) => i.id)).toEqual(["q1", "q3", "q2", "q4"]);
    expect(onReorder).toHaveBeenCalledTimes(1);
    expect(onReorder).toHaveBeenCalledWith(["q1", "q3", "q2", "q4"]);
  });

  it("reorders an upward drop and persists the new id order", async () => {
    const items = ref([{ id: "q1" }, { id: "q2" }, { id: "q3" }, { id: "q4" }]);
    const onReorder = vi.fn(() => Promise.resolve());
    const drag = useDragReorder({ items, onReorder });

    drag.onDragStart(items.value[3], fakeDragEvent()); // grab q4
    await drag.onDrop("q1"); // drop onto q1 (upward)

    expect(items.value.map((i) => i.id)).toEqual(["q4", "q1", "q2", "q3"]);
    expect(onReorder).toHaveBeenCalledTimes(1);
    expect(onReorder).toHaveBeenCalledWith(["q4", "q1", "q2", "q3"]);
  });

  it("does not persist when the drop target is the dragged item", async () => {
    const items = ref([{ id: "q1" }, { id: "q2" }]);
    const onReorder = vi.fn(() => Promise.resolve());
    const drag = useDragReorder({ items, onReorder });

    drag.onDragStart(items.value[0], fakeDragEvent());
    await drag.onDrop("q1");

    expect(items.value.map((i) => i.id)).toEqual(["q1", "q2"]);
    expect(onReorder).not.toHaveBeenCalled();
  });

  it("does not persist when the drop target is unknown", async () => {
    const items = ref([{ id: "q1" }, { id: "q2" }]);
    const onReorder = vi.fn(() => Promise.resolve());
    const drag = useDragReorder({ items, onReorder });

    drag.onDragStart(items.value[0], fakeDragEvent());
    await drag.onDrop("missing");

    expect(items.value.map((i) => i.id)).toEqual(["q1", "q2"]);
    expect(onReorder).not.toHaveBeenCalled();
  });

  it("clears the drag-over highlight and dragged id on drop", async () => {
    const items = ref([{ id: "q1" }, { id: "q2" }, { id: "q3" }]);
    const onReorder = vi.fn(() => Promise.resolve());
    const drag = useDragReorder({ items, onReorder });

    drag.onDragStart(items.value[0], fakeDragEvent());
    drag.onDragOver({ preventDefault: () => {} }, "q3");
    expect(drag.dragOverId.value).toBe("q3");
    expect(drag.draggedId.value).toBe("q1");

    await drag.onDrop("q3");

    expect(drag.dragOverId.value).toBeNull();
    expect(drag.draggedId.value).toBeNull();
  });

  it("takes the feed while idle, copied so the shadow never aliases it", () => {
    const feed = [{ id: "q1" }, { id: "q2" }];
    const items = ref([{ id: "q2" }, { id: "q1" }]);
    const drag = useDragReorder({ items, onReorder: vi.fn() });

    drag.syncFeed(feed, { isSaving: false });

    expect(items.value).toEqual(feed);
    expect(items.value).not.toBe(feed);
  });

  it("keeps the local order when the feed changes mid-drag", () => {
    const feed = [{ id: "q1" }, { id: "q2" }];
    const items = ref([{ id: "q2" }, { id: "q1" }]);
    const drag = useDragReorder({ items, onReorder: vi.fn() });

    drag.onDragStart(items.value[0], fakeDragEvent());
    drag.syncFeed(feed, { isSaving: false });

    expect(items.value).toEqual([{ id: "q2" }, { id: "q1" }]);
  });

  it("keeps the local order while a save is in flight", () => {
    const feed = [{ id: "q1" }, { id: "q2" }];
    const items = ref([{ id: "q2" }, { id: "q1" }]);
    const drag = useDragReorder({ items, onReorder: vi.fn() });

    drag.syncFeed(feed, { isSaving: true });

    expect(items.value).toEqual([{ id: "q2" }, { id: "q1" }]);
  });
});
