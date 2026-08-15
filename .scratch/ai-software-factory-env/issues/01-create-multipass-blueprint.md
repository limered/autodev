# 01 — Create multipass blueprint for disposable job VM

**What to build:** A declarative multipass blueprint/cloud-init file stored in this repo that can launch a disposable Ubuntu VM containing every tool an AI agent needs to implement a feature.

**Blocked by:** None — can start immediately.

**Status:** ready-for-agent

- [ ] A multipass blueprint or cloud-init file exists in the repo and is documented.
- [ ] Launching a VM from the blueprint succeeds on the local Windows host.
- [ ] The VM contains Git, Docker, opencode, .NET 8 SDK, Godot 4, Node.js, and an SSH client.
- [ ] A smoke test verifies each tool is on PATH and runnable.
