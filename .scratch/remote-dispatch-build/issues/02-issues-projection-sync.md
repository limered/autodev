# 02 — Issues projection: sync up and list eligible issues

**What to build:** the client can push a repo's `ready-for-agent` issues to the backend, and the web shows them as a list of eligible issues across repos. This is the left half of the dispatch view and the inbound-data half of the whole feature.

**Blocked by:** None — can start immediately.

**Status:** ready-for-agent

- [ ] A new `issues` table stores synced issues, keyed by the GitHub issue id, with repo, number, title, html_url, labels, body, state, updated_at (created idempotently at boot like the existing `runs` schema).
- [ ] A token-authed `PUT /issues/{repo}` accepts the full current set of `ready-for-agent` issues for that repo and does a full-replace-per-repo in one transaction (delete issues no longer present for that repo, upsert the rest). Same shared-secret header as the existing ingest.
- [ ] A public `GET /issues` returns all synced issues.
- [ ] The dispatch view renders the **eligible issues** column: each row shows title and `repo#number`, grouped/listed across all repos.
- [ ] Demoable: `PUT` a snapshot for a repo, then `PUT` a smaller snapshot, and the web list reflects both adds and removals.
- [ ] Local flow unchanged: existing `runs`/`/runs` behaviour and the current dashboard are untouched.
