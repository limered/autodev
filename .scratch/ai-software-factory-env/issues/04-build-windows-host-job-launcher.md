# 04 — Build Windows host job launcher

**What to build:** A Windows PowerShell script that takes a repository URL and an issue/spec as input, provisions a disposable job VM, runs the opencode agent against it, detects the resulting branch/PR, and destroys the VM.

**Blocked by:** 02 — Configure bot Git authentication; 03 — Define opencode headless job agent.

**Status:** done

- [x] `start-job.ps1` accepts a repository URL and a feature description/spec as parameters.
  - `-RepoUrl` (git@github.com:owner/name.git) and `-Spec`; owner/name parsed from the URL for PR polling.
- [x] The script launches a fresh multipass VM using the project blueprint.
- [x] The script provisions the VM with the bot credentials and the target repository.
  - SSH key, PAT, opencode API key, and `.opencode` config injected; repo cloned by `test-feature-builder.sh` (now takes `REPO` as an arg).
- [x] The script runs the opencode agent inside the VM and waits for completion.
- [x] The script detects the created branch/PR by polling GitHub and reports it to the caller.
  - Polls the pulls API, prints the PR URL, returns a result object (Repo/Branch/PrUrl/PrNumber), exit 1 on failure.
- [x] The script destroys the VM after the job finishes or fails.
  - Always destroys; `-KeepVmOnFailure` leaves it up for debugging on failure.

Verified end to end against `limered/autodev` (kimi-k2.7-code): created PR #3, VM destroyed. `test-feature-builder.ps1` is now a thin wrapper over `start-job.ps1`.
