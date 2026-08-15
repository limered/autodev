# 04 — Build Windows host job launcher

**What to build:** A Windows PowerShell script that takes a repository URL and an issue/spec as input, provisions a disposable job VM, runs the opencode agent against it, detects the resulting branch/PR, and destroys the VM.

**Blocked by:** 02 — Configure bot Git authentication; 03 — Define opencode headless job agent.

**Status:** ready-for-agent

- [ ] `start-job.ps1` accepts a repository URL and a feature description/spec as parameters.
- [ ] The script launches a fresh multipass VM using the project blueprint.
- [ ] The script provisions the VM with the bot credentials and the target repository.
- [ ] The script runs the opencode agent inside the VM and waits for completion.
- [ ] The script detects the created branch/PR by polling GitHub and reports it to the caller.
- [ ] The script destroys the VM after the job finishes or fails.
