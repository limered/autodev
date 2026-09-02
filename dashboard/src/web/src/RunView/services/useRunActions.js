import { ref } from "vue";

// Write seam for runs, mirroring useQueueActions: the caller injects fetch so the
// try/!res.ok/error/finally dance is written and tested once. Only action today is
// deleteRun, used to clear stuck runs (e.g. a run wedged in "launching").
export function useRunActions(fetchFn = fetch) {
  const error = ref(null);
  const isBusy = ref(false);

  async function deleteRun(runId) {
    if (isBusy.value) return false;
    isBusy.value = true;
    error.value = null;
    try {
      const res = await fetchFn(`/runs/${runId}`, { method: "DELETE" });
      if (!res.ok) throw new Error(`HTTP ${res.status}`);
      return true;
    } catch (e) {
      error.value = e.message;
      return false;
    } finally {
      isBusy.value = false;
    }
  }

  return { deleteRun, deleteError: error, isDeleting: isBusy };
}
