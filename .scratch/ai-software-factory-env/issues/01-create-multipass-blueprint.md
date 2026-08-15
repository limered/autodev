# 01 — Create multipass blueprint for disposable job VM

**What to build:** A declarative multipass blueprint/cloud-init file stored in this repo that can launch a disposable Ubuntu VM containing every tool an AI agent needs to implement a feature.

**Blocked by:** None — can start immediately.

**Status:** completed

- [x] A multipass blueprint or cloud-init file exists in the repo and is documented.
- [x] Launching a VM from the blueprint succeeds on the local Windows host.
- [x] The VM contains Git, Docker, opencode, .NET 8 SDK, Node.js, and an SSH client.
- [ ] Godot 4 is installed and runnable. *(deferred — downloads fail with TLS errors inside multipass VMs on this host; see ADR-001)*
- [x] A smoke test verifies each installed tool is on PATH and runnable.
