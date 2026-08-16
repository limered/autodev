# 05 — End-to-end smoke test

**What to build:** A documented, repeatable smoke test that exercises the full AI Software Factory environment against a sample repository and proves a real pull request is created.

**Blocked by:** 04 — Build Windows host job launcher.

**Status:** done

- [x] A sample repository and issue/spec are chosen or created for the smoke test.
  - Ticket 06 (emit heartbeat) used as the real payload against `limered/autodev`.
- [x] Running `start-job.ps1` with the sample input completes without manual intervention.
- [x] A pull request is created in the sample repository by the bot user.
  - PR #4: https://github.com/limered/autodev/pull/4
- [x] The PR contains the expected change described in the input spec.
  - `test-feature-builder.sh` wraps `opencode run` to touch `/tmp/heartbeat` per output line.
