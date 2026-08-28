# 05 — Start next: claim the top queued item and run it on this PC

**What to build:** the whole loop closes. The user clicks "start next" in the web; a persistent client on this PC notices, claims the top queued item, and runs it through the existing VM factory; the run is correlated back to its queue item and visible as a live run. One at a time.

**Blocked by:** 03 (queue), 01 (`-RunId` param on `start-job.ps1`).

**Status:** ready-for-agent

- [ ] `POST /queue/{id}/start-next` records a start-requested intent on the highest-ranked queued item. Web-write, no token. The web's **Start next** button is disabled while any queue item is in the running state (one-at-a-time).
- [ ] `POST /queue/claim-next` (token-authed) atomically claims the highest-ranked item that is start-requested and has no run yet: it sets the queue row's run id (backend-generated) and returns that run id plus the repo/issue needed to run. Concurrent calls cannot double-claim. Claim **reserves the run id only** — it does not insert a runs row.
- [ ] A new `dispatch-client.ps1` runs as a foreground loop on the host: on a slow cadence it syncs each configured repo's `ready-for-agent` issues up (ticket 02 endpoint); on a fast cadence it calls `claim-next`, and when it gets a claim it invokes `start-job.ps1 -RunId <claimed id>` **synchronously** (the blocking call enforces one-at-a-time), then resumes.
- [ ] The client reads a local, gitignored config file for the watched-repo list, backend URL, and intervals; the GitHub token is read from the existing `.secrets` location, not the config.
- [ ] The client never crashes on a transient GitHub/backend error — each phase is guarded and the next tick retries.
- [ ] The existing `run-started` event creates the runs row as it does today; the web correlates a queue item to its run by the shared run id and offers a link to the run card. A brief "starting…" state covers the gap between claim and the first run event.
- [ ] When a run reaches **done**, the queue row for that run id is deleted — folded into the existing event handler and a **no-op when no queue row matches** (hand-run jobs are unaffected). A **failed** run leaves the queue row in place.
- [ ] Demoable end-to-end: enqueue an issue, click Start next, a VM job runs on this PC, the run appears in the dashboard linked to the queue item, and on success the item leaves the queue.
- [ ] Local flow unchanged: `start-job.ps1` / `start-issue.ps1` still hand-runnable; hand-run jobs produce no queue side effects; the event fold change is a pure no-op for them.
