# 02 — Configure bot Git authentication

**What to build:** A secure way to inject the bot user's GitHub SSH key and Personal Access Token into a fresh job VM so the agent can clone repositories, push branches, and create pull requests.

**Blocked by:** 01 — Create multipass blueprint for disposable job VM.

**Status:** completed

- [x] The bot SSH private key is available to the Windows host and is injected into the VM at launch.
- [x] The GitHub PAT is available to the Windows host and is injected into the VM at launch.
- [x] Inside the VM, the bot can shallow-clone a GitHub repository over SSH.
- [x] Inside the VM, the bot can push a branch to GitHub over SSH.
- [x] Inside the VM, the bot can create a pull request via the GitHub API using the PAT.

## Verification run

Run: `test-bot-auth.ps1` launched a fresh VM, injected the SSH key and PAT, and executed `test-bot-auth.sh`.

Results:
- PASS: SSH key present in `~/.ssh/bot-github` with permissions 600.
- PASS: GitHub added to `~/.ssh/known_hosts`.
- PASS: PAT injected into the VM.
- PASS: Shallow clone of `git@github.com:limered/autodev.git` over SSH.
- PASS: Branch `bot-auth-test-20260815-225540` pushed to GitHub.
- PASS: PR created: https://github.com/limered/autodev/pull/1
- PASS: VM destroyed afterward.

## Note

A test PR and branch were created during verification. They can be closed/deleted manually.
