# 05 — End-to-end smoke test

**What to build:** A documented, repeatable smoke test that exercises the full AI Software Factory environment against a sample repository and proves a real pull request is created.

**Blocked by:** 04 — Build Windows host job launcher.

**Status:** ready-for-agent

- [ ] A sample repository and issue/spec are chosen or created for the smoke test.
- [ ] Running `start-job.ps1` with the sample input completes without manual intervention.
- [ ] A pull request is created in the sample repository by the bot user.
- [ ] The PR contains the expected change described in the input spec.
