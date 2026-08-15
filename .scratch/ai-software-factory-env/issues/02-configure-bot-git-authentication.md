# 02 — Configure bot Git authentication

**What to build:** A secure way to inject the bot user's GitHub SSH key and Personal Access Token into a fresh job VM so the agent can clone repositories, push branches, and create pull requests.

**Blocked by:** 01 — Create multipass blueprint for disposable job VM.

**Status:** ready-for-agent

- [ ] The bot SSH private key is available to the Windows host and is injected into the VM at launch.
- [ ] The GitHub PAT is available to the Windows host and is injected into the VM at launch.
- [ ] Inside the VM, the bot can shallow-clone a GitHub repository over SSH.
- [ ] Inside the VM, the bot can push a branch to GitHub over SSH.
- [ ] Inside the VM, the bot can create a pull request via the GitHub API using the PAT.
