# 03 — Test phase with the harness contract

**What to build:** A test phase runs between implement and PR, occupying the hook point. A new test-runner agent reads the target repo's `AGENTS.md` for `test-harness.<name>: <command>` entries and runs every one. If no harness is declared, the run fails fast. If any harness goes red, the run fails: freeze snapshot and VM teardown happen as with any failure, the implemented branch stays pushed, and no PR is created. Only when every declared harness passes does the run continue to the PR phase. Looping failures back to the implement agent is out of scope.

**Blocked by:** 02 — Split PR creation into its own phase.

**Status:** ready-for-agent

- [ ] test-runner agent definition exists and runs in the same clone between implement and PR
- [ ] It parses all `test-harness.<name>: <command>` lines from the target repo's AGENTS.md
- [ ] No declared harness fails the run fast with a reason
- [ ] Every declared harness is run; any failure fails the run
- [ ] On test failure: no PR, branch remains pushed, freeze snapshot captured, VM torn down, recorded as failed
- [ ] All harnesses green advances to the PR phase
