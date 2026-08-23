# 02 — Split PR creation into its own phase

**What to build:** The feature-builder agent stops creating the PR itself. Implementation and PR creation become two separate agent invocations in the same clone. After the implement phase, the run proceeds to the PR phase only if the implement agent exited cleanly and the branch has commits ahead of base. A new pr-author agent then runs as a second `opencode run`, authors the PR title and body from the diff, and POSTs the PR. A clearly-marked hook point sits between the two phases for future steps to occupy.

**Blocked by:** 01 — Standard prompt in the agent, issue token as the only input.

**Status:** ready-for-agent

- [x] feature-builder no longer POSTs a PR
- [x] A new pr-author agent definition exists and creates the PR from the branch diff
- [x] PR phase runs as a distinct `opencode run` after the implement phase
- [x] Gate between phases: proceed only if implement exited 0 AND the branch has commits ahead of base
- [x] A clean-but-empty implement (no commits) stops before the PR phase and is recorded as failed
- [x] An explicit, commented hook point exists between implement and PR
- [x] Host PR-verify still confirms the PR after the run

_Verification: runner phase/gate/hook logic exercised in a sandbox (exact script tail, stubbed opencode, local git repo): healthy run, clean-but-empty, implement-failure, pr-failure, missing-base-ref all behave as specified; `dotnet test dashboard/src/Api.Tests` green. No multipass VM run performed, so the live pr-author POST is unexercised end to end._
