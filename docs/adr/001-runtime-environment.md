# ADR 001: slop-factory — Runtime Environment

## Status
Proposed

## Context
We want a disposable, isolated runtime where an AI agent can check out a repository, implement a feature, and produce a pull request. The runtime must run on a Windows host today and be movable to cloud infrastructure later.

Key requirements that drove this decision:
- Need to run Docker (e.g., for database-dependent end-to-end tests).
- Need port isolation so multiple jobs/services can bind the same local port without conflict.
- Host machine runs Windows; operator wants to stay on Windows for now.
- The agent is opencode running in headless/oneshot mode (`opencode run "prompt"`).
- Job input: an issue/goal/spec. Job output: a branch and pull request.
- Orchestrator database is out of scope for the MVP; job state is communicated through Git.

## Decision
Use a Linux VM running on the Windows host as the execution environment for each job.

- **Hypervisor:** multipass, using its default backend on Windows (Hyper-V on Pro, VirtualBox/QEMU on Home).
- **One VM per job/feature**, with 2–3 jobs expected to run in parallel on the local PC.
- **VM is disposable**; all persistent state is pushed to Git before destruction.
- **Base image:** Ubuntu LTS pre-baked with Git, Docker, opencode, SSH client, .NET 8 SDK, Node.js, and the bot SSH key. Godot 4 is deferred because downloads fail with TLS errors inside multipass VMs on the current Windows host.
- **Repository access** via a single global SSH key tied to a dedicated bot GitHub user; shallow clone at job start.
- **Pull request creation** uses a GitHub Personal Access Token stored in the VM.
- **VM image build** via a multipass blueprint/cloud-init file stored in this repository.
- **Job trigger** via a Windows host PowerShell script (`start-job.ps1`) that launches the VM and passes repo/issue/spec; a web UI/API will replace this later.
- **opencode agents/skills** are pulled from this repository (`autodev`) into the project repository at job start.
- **Job completion signal** is detected by polling for the branch/PR on GitHub; a service callback to a future web UI will replace this later.

## Consequences

### Positive
- Full Linux environment avoids Windows tooling friction for opencode, Docker, and common dev stacks.
- Real network isolation per VM removes localhost port collisions between jobs.
- Disposable per-job VM limits blast radius of a misbehaving agent or build.
- Git-based state keeps the MVP simple; no external orchestrator database required.

### Negative / Risks
- VM startup time is slower than containers; per-job latency will be noticeable.
- Running multiple Linux VMs in parallel on a local Windows machine is resource-heavy.
- A global SSH key inside the VM is readable by agent code; compromise of one job compromises the bot account.
- "Local now, cloud later" may require reworking VM provisioning if the local hypervisor choice is too Windows-specific.

## Open Questions
1. What is the exact opencode command line and configuration layout for a headless job?
2. How are agents/skills copied from `autodev` into the project repo inside the VM? (rsync, git submodule, file copy, opencode skill path?)
3. What is the minimum viable multipass blueprint spec, and does it support all required packages out of the box?
4. How does `start-job.ps1` clean up the VM after the job, and how does it report success/failure to the caller?
5. What naming convention is used for branches and PRs so multiple jobs do not collide?
