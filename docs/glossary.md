# Glossary

| Term | Definition |
|---|---|
| **AI Software Factory** | A system that uses AI agents to automatically implement software features from specifications and produce pull requests. |
| **Job** | A single unit of work: check out a repository, run opencode against a goal/spec, and produce a branch/PR. |
| **Runtime Environment** | The isolated execution context where a job runs. In the MVP this is a Linux VM on a Windows host. |
| **opencode** | The headless AI agent CLI used inside the runtime; invoked as `opencode run "prompt"`. |
| **Orchestrator DB** | Out-of-scope external database that would track job state and health. For the MVP, Git is the source of truth. |
| **Bot User** | A dedicated GitHub user account used by all jobs to clone repositories and push branches via a shared SSH key. |
| **GitHub PAT** | Personal Access Token stored in the VM, used by opencode to create pull requests via the GitHub API. |
| **multipass blueprint** | A declarative cloud-init/multipass configuration that defines how the job VM is provisioned. |
| **autodev repo** | This repository; contains the environment setup, blueprints, and opencode agents/skills used by jobs. |
| **project repo** | The target repository that a job checks out and modifies. |
| **Disposable VM** | A virtual machine that is created for one job and destroyed afterward; no persistent local state. |
