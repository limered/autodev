import { ref } from "vue";

// Write seam for runs, mirroring useQueueActions: the caller injects fetch so the
// try/!res.ok/error/finally dance is written and tested once. Actions are
// deleteRun, used to clear stuck runs (e.g. a run wedged in "launching"), and
// restartRun, which enqueues a same-branch resume of a failed run.
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

  async function restartRun(runId) {
    if (isBusy.value) return null;
    isBusy.value = true;
    error.value = null;
    try {
      const res = await fetchFn(`/runs/${runId}/restart`, { method: "POST" });
      if (!res.ok) throw new Error(`HTTP ${res.status}`);
      return await res.json();
    } catch (e) {
      error.value = e.message;
      return null;
    } finally {
      isBusy.value = false;
    }
  }

  return { deleteRun, deleteError: error, isDeleting: isBusy, restartRun, restartError: error, isRestarting: isBusy };
}
