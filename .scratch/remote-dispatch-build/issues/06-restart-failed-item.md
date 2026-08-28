# 06 — Restart a failed queue item

**What to build:** a failed item stays in the queue and the user can put it back in line to run again, without re-enqueueing it from scratch.

**Blocked by:** 05 (a run must be able to fail, and start-next must exist to re-run it).

**Status:** ready-for-agent

- [ ] `POST /queue/{id}/restart` clears the queue row's run id, returning the item to the `queued` (inferred) state at its existing rank. Web-write, no token.
- [ ] The dispatch view shows a **Restart** control only on items whose linked run is failed.
- [ ] After restart, the item is eligible for the next Start next / claim.
- [ ] Demoable: a failed queue item, click Restart, it shows as queued again and can be started.
- [ ] Local flow unchanged.
