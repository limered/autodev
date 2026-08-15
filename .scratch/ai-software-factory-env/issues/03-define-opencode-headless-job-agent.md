# 03 — Define opencode headless job agent

**What to build:** An opencode agent/prompt configuration that runs headlessly inside a job VM, implements a change in a checked-out project repository, pushes the result as a branch, and creates a pull request.

**Blocked by:** 01 — Create multipass blueprint for disposable job VM; 02 — Configure bot Git authentication.

**Status:** ready-for-agent

- [ ] An opencode agent or skill configuration exists for the headless feature-implementation workflow.
- [ ] A mechanism copies the relevant agents/skills from the `autodev` repo into the project repo inside the VM.
- [ ] Running the agent against a sample repository produces a branch containing the requested change.
- [ ] The agent creates a pull request for the branch via the GitHub API.
