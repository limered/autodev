import { describe, it, expect, vi } from "vitest";
import { nextTick, ref } from "vue";
import { useQueueFeedShadow } from "../../services/useQueueFeedShadow.js";

function fakeDragEvent() {
  return { dataTransfer: { effectAllowed: null, setData: vi.fn() } };
}

describe("useQueueFeedShadow", () => {
  it("takes the feed while idle, copied so the shadow never aliases it", async () => {
    const feed = ref([{ id: "q1" }, { id: "q2" }]);
    const isSaving = ref(false);
    const shadow = useQueueFeedShadow({ feed: () => feed.value, isSaving, onReorder: vi.fn() });

    expect(shadow.localQueue.value).toEqual([{ id: "q1" }, { id: "q2" }]);
    expect(shadow.localQueue.value).not.toBe(feed.value);

    feed.value = [{ id: "q9" }];
    await nextTick();

    expect(shadow.localQueue.value).toEqual([{ id: "q9" }]);
  });

  it("holds the shadow while a save is in flight and takes the feed after", async () => {
    const feed = ref([{ id: "q1" }, { id: "q2" }]);
    const isSaving = ref(false);
    const shadow = useQueueFeedShadow({ feed: () => feed.value, isSaving, onReorder: vi.fn() });

    isSaving.value = true;
    feed.value = [{ id: "q9" }];
    await nextTick();

    expect(shadow.localQueue.value).toEqual([{ id: "q1" }, { id: "q2" }]);

    isSaving.value = false;
    feed.value = [{ id: "q9" }];
    await nextTick();

    expect(shadow.localQueue.value).toEqual([{ id: "q9" }]);
  });

  it("keeps the local order when the feed changes mid-drag", async () => {
    const feed = ref([{ id: "q1" }, { id: "q2" }]);
    const isSaving = ref(false);
    const shadow = useQueueFeedShadow({ feed: () => feed.value, isSaving, onReorder: vi.fn() });

    shadow.onDragStart(shadow.localQueue.value[0], fakeDragEvent());
    feed.value = [{ id: "q2" }, { id: "q1" }];
    await nextTick();

    expect(shadow.localQueue.value).toEqual([{ id: "q1" }, { id: "q2" }]);
  });

  it("moves rows optimistically and persists the new id order on drop", async () => {
    const feed = ref([{ id: "q1" }, { id: "q2" }, { id: "q3" }]);
    const isSaving = ref(false);
    const onReorder = vi.fn(() => Promise.resolve());
    const shadow = useQueueFeedShadow({ feed: () => feed.value, isSaving, onReorder });

    shadow.onDragStart(shadow.localQueue.value[0], fakeDragEvent());
    await shadow.onDrop("q3");

    expect(shadow.localQueue.value.map((i) => i.id)).toEqual(["q2", "q1", "q3"]);
    expect(onReorder).toHaveBeenCalledWith(["q2", "q1", "q3"]);
  });
});
