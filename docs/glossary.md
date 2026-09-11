# Glossary

| Term | Definition |
|---|---|
| **slop-factory** | A system that uses AI agents to automatically implement software features from specifications and produce pull requests. |
| **Job** | A single unit of work: check out a repository, run opencode against a goal/spec, and produce a branch/PR. |
| **Runtime Environment** | The isolated execution context where a job runs. In the MVP this is a Linux VM on a Windows host. |
| **opencode** | The headless AI agent CLI used inside the runtime; invoked as `opencode run "prompt"`. |
| **Orchestrator DB** | Out-of-scope external database that would track job state and health. For the MVP, Git is the source of truth. |
| **Bot User** | A dedicated GitHub user account used by all jobs to clone repositories and push branches via a shared SSH key. |
| **GitHub PAT** | Personal Access Token stored in the VM, used by opencode to read issues, create pull requests, and update issue bodies via the GitHub API. |
| **multipass blueprint** | A declarative cloud-init/multipass configuration that defines how the job VM is provisioned. |
| **slop-factory repo** | This repository (hosted at `limered/autodev`); contains the environment setup, blueprints, and opencode agents/skills used by jobs. |
| **project repo** | The target repository that a job checks out and modifies. |
| **Disposable VM** | A virtual machine that is created for one job and destroyed afterward; no persistent local state. |
| **Agent liveness** | Whether the agent inside a job VM is making forward progress. Distinct from VM liveness: the VM can be healthy while the agent is stuck. This is the failure the heartbeat system detects. |
| **Heartbeat** | A marker file in the job VM, touched by a wrapper only when the agent emits new output. A stale marker means the agent has stopped making progress. The host polls its freshness; staleness beyond 5 minutes signals a stall. |
| **Freeze snapshot** | A JSON manifest of the VM's state (agent log tail, marker timestamp, job params, `ps aux`/`free -m`/`df -h`) captured on the host when a stall is detected, before the VM is destroyed, so the freeze can be debugged later. Stored under `.scratch/freezes/<job>-<timestamp>/`. |
| **pipeline shape** | The ordered stage catalog of a job: stage ids, member workers, and loop iteration counts in `agents.json` map order. Owned by `lib/AgentsConfig.ps1` (ADR 003). |
| **slot** | The seeded category a finished `(agent, iteration)` phase fills per `Get-StepCategory`. Overflow clamps to the last matching slot. |
| **candidate** | One `(agent, iteration)` phase entry derived from pipeline shape for relay (`Get-PhaseStepCandidates`). |
| **uncategorized** | The bucket for stages and steps with no configured category. Detail rows for unmapped workers group here; the bucket never matches a heartbeat report, so it cannot light a stage. Literal pinned by contract test per tier (PS, API, RunView). |
| **dev loop** | The `RunView` projection of per-worker step rows with loop affordance, totals, and progress (`RunView/models/devLoop.js`). Composed by `runView`; loop membership comes from seeded stage `type`, never a frontend hardcode. |
