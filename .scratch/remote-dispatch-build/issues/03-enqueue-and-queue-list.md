# 03 — Enqueue an issue and list the run queue

**What to build:** the user can move an eligible issue into an ordered run queue, and the web shows that queue as a distinct list with per-item status. This creates the queue itself; ordering, starting, and restarting come in later tickets.

**Blocked by:** 02 (issues projection must exist to reference and join).

**Status:** ready-for-agent

- [ ] A new `queue` table stores queue rows: id, a reference to the issue id, an integer rank, a nullable run id, a nullable start-requested timestamp, enqueued-at (created idempotently at boot).
- [ ] `POST /queue` enqueues an eligible issue, appended at the end of the order (rank = current max + 1). Web-write, no auth token (matches the public-dashboard stance).
- [ ] A public `GET /queue` returns queue rows ordered by rank, **joined to the issues table for live title/repo text** (reference, not copy — an edited issue body reflects without re-enqueue).
- [ ] Queue-item **status is inferred**, not stored: no linked run = queued; a linked run that is launching/running = running; failed/done taken from the linked run.
- [ ] The dispatch view renders the **run queue** column (right side): rank, issue title/repo, an inferred-status pill. An eligible issue already in the queue is omitted from the eligible column. Each row has an **Enqueue →** control in the eligible column.
- [ ] A queued item whose issue is no longer present in the projection (join miss) renders as struck-through "no longer eligible" rather than vanishing.
- [ ] Demoable: click Enqueue on an eligible issue, it appears in the queue as `queued` and leaves the eligible list.
- [ ] Local flow unchanged.
