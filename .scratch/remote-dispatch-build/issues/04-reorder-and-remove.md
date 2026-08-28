# 04 — Prioritize the queue: drag to reorder and remove

**What to build:** the user can reorder the run queue by dragging items and remove items from it, with the order persisting. This makes the queue an intentional, hand-prioritized list.

**Blocked by:** 03 (queue must exist to reorder).

**Status:** ready-for-agent

- [ ] `PATCH /queue/order` accepts the full ordered list of queue-item ids and rewrites their ranks in one transaction. Web-write, no auth token.
- [ ] `DELETE /queue/{id}` removes a queue item.
- [ ] The dispatch view supports drag-to-reorder on the queue column; dropping persists the new order via the reorder endpoint.
- [ ] Each queue row has a **Remove** control.
- [ ] Demoable: drag a lower item to the top, reload the page, the new order survives; remove an item, it disappears.
- [ ] Local flow unchanged.
