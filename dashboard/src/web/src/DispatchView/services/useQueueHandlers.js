import { useQueueActions } from "./useQueueActions.js";

// The run()-then-load() wiring the DispatchView columns share (Block A): each
// queue write is followed by the feed reloads that show its effect, written
// once here instead of re-stated per column and per test. The columns hand in
// their two feed loads (plus an injectable fetch for the seam tests) and get
// back handlers ready to bind in the template, alongside the per-action
// error/busy refs those templates already render.
//
// Reload policy, preserved exactly from the columns:
// - enqueue/remove refresh both feeds — the queue gains/loses a row and the
//   eligible-issue set moves with it — and only on success.
// - startNext/restart refresh the queue feed only, and only on success.
// - reorder refreshes the queue feed unconditionally: a drop persists
//   optimistically (useDragReorder already moved the rows), so the shadow
//   copy must reconverge on server truth even when the persist failed.
// Handlers take the domain objects the templates iterate (an issue or a queue
// item, both carrying their id); the Start-next head/gating guard is view
// state and stays in RunQueueColumn.
export function useQueueHandlers({ reloadIssues, reloadQueue, fetchFn = fetch } = {}) {
  const actions = useQueueActions(fetchFn);

  async function enqueue(issue) {
    if (await actions.enqueue(issue.gitHubId)) {
      await Promise.all([reloadIssues(), reloadQueue()]);
    }
  }

  async function remove(item) {
    if (await actions.remove(item.id)) {
      await Promise.all([reloadIssues(), reloadQueue()]);
    }
  }

  async function startNext(item) {
    if (await actions.startNext(item.id)) await reloadQueue();
  }

  async function restart(item) {
    if (await actions.restart(item.id)) await reloadQueue();
  }

  async function reorder(ids) {
    await actions.reorder(ids);
    await reloadQueue();
  }

  return {
    enqueue,
    remove,
    startNext,
    restart,
    reorder,
    enqueueError: actions.enqueueError,
    isEnqueueing: actions.isEnqueueing,
    reorderError: actions.reorderError,
    isReordering: actions.isReordering,
    removeError: actions.removeError,
    isRemoving: actions.isRemoving,
    startNextError: actions.startNextError,
    isStartingNext: actions.isStartingNext,
    restartError: actions.restartError,
    isRestarting: actions.isRestarting,
  };
}
