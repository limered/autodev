# 01 — Standard prompt in the agent, issue token as the only input

**What to build:** A factory job is launched with just an issue token of the form `feature-slug/NN` instead of a free-form spec string. The feature-builder agent carries its standard implementation instructions in its own definition, so the run input shrinks to the issue token. Inside the VM the agent locates the matching issue file in the cloned target repo's tracker layout (`.scratch/<feature-slug>/issues/<NN>-*.md`), reads it, and implements it. If the issue can't be found in the clone, the run fails fast with a clear reason.

**Blocked by:** None — can start immediately.

**Status:** ready-for-agent

- [x] The in-VM runner passes only the issue token across the exec boundary — no spec body
- [x] Standard implement instructions live in the feature-builder agent definition, not assembled inline in the shell
- [x] Given `feature-slug/NN`, the agent resolves and reads `.scratch/<feature-slug>/issues/<NN>-*.md` from the clone
- [x] A missing/unresolvable issue fails the run fast with a reason surfaced to the dashboard/freeze
- [ ] An unambiguous end-to-end run implements a real issue from token alone _(not yet verified — no VM run performed)_
