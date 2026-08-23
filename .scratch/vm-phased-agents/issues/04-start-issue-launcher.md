# 04 — start-issue.ps1 launcher

**What to build:** A thin `start-issue.ps1` lets a user launch a factory job by issue name. It takes an issue token `feature-slug/NN` and an explicit `-RepoUrl`, derives the branch name from the issue reference, and calls the existing `start-job.ps1` passing the issue token through. It reads no issue content on the host — the token alone crosses into the VM, where the implement agent resolves the file. `start-job.ps1`'s own interface stays spec/token-driven and unchanged.

**Blocked by:** 01 — Standard prompt in the agent, issue token as the only input.

**Status:** ready-for-agent

- [x] `start-issue.ps1` accepts `feature-slug/NN` and `-RepoUrl`
- [x] Branch is auto-derived from the issue reference
- [x] It invokes `start-job.ps1` passing the issue token as the run input
- [x] Host never reads issue file content
- [x] Launching by issue token runs a full job end to end _(verified: tickets 02–04 were implemented by the factory VM itself — commits authored by AI Software Factory Bot)_
