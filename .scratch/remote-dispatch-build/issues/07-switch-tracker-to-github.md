# 07 — Switch the issue tracker from local markdown to GitHub

**What to build:** the project's issue tracker becomes GitHub Issues on the autodev repo instead of local markdown files under `.scratch/`. After this ticket, new tickets are authored as GitHub issues, the tracker docs say so, and the agent skills that read/launch issues target GitHub. This unblocks authoring the full-flow test issue (08) natively on GitHub.

**Blocked by:** None for correctness, but sequence **after 01–06 are implemented** — migrating the tracker mid-flight would move the very tickets being worked. Do this once the dispatch build tickets are done.

**Status:** ready-for-agent

- [ ] `docs/agents/issue-tracker.md` is rewritten to declare **GitHub Issues (autodev repo)** as the tracker: how tickets are created, how dependency/blocking edges are expressed (native "blocked by" / task-list references), and the `ready-for-agent` label convention.
- [ ] `AGENTS.md` still points at the tracker doc and needs no contradicting statement; verify no other doc (e.g. glossary, ADRs) still asserts local-markdown tracking. (Freeze snapshots under `.scratch/freezes/` are unrelated and stay.)
- [ ] The `start-issue` launcher no longer assumes a local `.scratch/<slug>/issues/<NN>-*.md` path: it resolves an issue by its GitHub identifier (issue number/url), reads the issue body from GitHub, and builds the spec from it. Local-markdown resolution is removed or clearly superseded.
- [ ] Backwards-compat for the VM flow is preserved: `start-job.ps1` / `start-issue.ps1` still launch a job on this PC and open a PR; only the *source of the issue text* changes from a local file to a GitHub issue.
- [ ] A short migration note records that existing `.scratch/*/issues/` folders are historical; no requirement to back-fill closed feature folders.
- [ ] Demoable: create a `ready-for-agent` issue on GitHub, run the launcher against its number, and a VM job starts from that issue's text.
