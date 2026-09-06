import { computed, ref } from "vue";

// Write seam for the run queue, mirroring the read composables (use*Feed): the
// caller injects fetch, so the same try/!res.ok/error/finally dance is written once
// and tested through this interface instead of five times inline in the component.
// Each action owns its own error + in-flight refs so the view keeps its distinct banners.
export function useQueueActions(fetchFn = fetch) {
  function action() {
    const error = ref(null);
    const isBusy = ref(false);
    async function run(fn) {
      if (isBusy.value) return false;
      isBusy.value = true;
      error.value = null;
      try {
        const res = await fn();
        if (!res.ok) throw new Error(`HTTP ${res.status}`);
        return true;
      } catch (e) {
        error.value = e.message;
        return false;
      } finally {
        isBusy.value = false;
      }
    }
    return { error, isBusy, run };
  }

  const enqueueA = action();
  const reorderA = action();
  const removeA = action();
  const startNextA = action();
  const restartA = action();

  const isSaving = computed(
    () =>
      enqueueA.isBusy.value ||
      reorderA.isBusy.value ||
      removeA.isBusy.value ||
      startNextA.isBusy.value ||
      restartA.isBusy.value,
  );

  return {
    enqueue: (issueId) =>
      enqueueA.run(() =>
        fetchFn("/queue", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ issueId }),
        }),
      ),
    enqueueError: enqueueA.error,
    isEnqueueing: enqueueA.isBusy,

    reorder: (ids) =>
      reorderA.run(() =>
        fetchFn("/queue/order", {
          method: "PATCH",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ ids }),
        }),
      ),
    reorderError: reorderA.error,
    isReordering: reorderA.isBusy,

    remove: (id) => removeA.run(() => fetchFn(`/queue/${id}`, { method: "DELETE" })),
    removeError: removeA.error,
    isRemoving: removeA.isBusy,

    startNext: (id) => startNextA.run(() => fetchFn(`/queue/${id}/start-next`, { method: "POST" })),
    startNextError: startNextA.error,
    isStartingNext: startNextA.isBusy,

    restart: (id) => restartA.run(() => fetchFn(`/queue/${id}/restart`, { method: "POST" })),
    restartError: restartA.error,
    isRestarting: restartA.isBusy,
    isSaving,
  };
}
