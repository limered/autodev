# autodev

An AI Software Factory: it runs [opencode](https://opencode.ai) agents inside
disposable Linux VMs to implement features from a spec and open a pull request,
then destroys the VM. Git and GitHub are the source of truth — there's no
persistent orchestrator state in the MVP.

## How a job works

1. `start-job.ps1` launches a fresh multipass VM from the cloud-init blueprint.
2. It provisions the VM with the bot SSH key, a GitHub PAT, the opencode API key, and the local `.opencode` config.
3. The opencode feature-builder agent clones the target repo, implements the spec, and pushes a branch/PR.
4. A heartbeat marker is polled from the host; a stalled agent triggers a freeze snapshot before teardown.
5. The PR URL is printed and the VM is destroyed.

## Prerequisites

- Windows host with [multipass](https://multipass.run) installed
- PowerShell 5.1+
- Secrets in `.secrets/`: `github-pat.txt`, `opencode-api-key.txt`

## Usage

```powershell
# Run a job from a spec / issue token
./start-job.ps1 -RepoUrl https://github.com/owner/name.git -Spec "42"

# Start a tracked GitHub issue by number
./start-issue.ps1 -Issue 42
```

## Layout

| Path | Purpose |
|---|---|
| `start-job.ps1` | Launch a VM and run a feature-builder job end to end |
| `start-issue.ps1` | Resolve a GitHub issue and dispatch a job for it |
| `.opencode/` | Agent, skill, and model config shipped into each VM |
| `infrastructure/multipass/` | cloud-init blueprint and in-VM job script |
| `dashboard/` | Web dashboard + API for run reporting |
| `docs/adr/` | Architecture decision records |
| `docs/glossary.md` | Project vocabulary |
| `.scratch/freezes/` | Freeze snapshots captured on stall |

## Tests

```powershell
dotnet test dashboard/src/Api.Tests
npm test --prefix dashboard/src/web
```

See `AGENTS.md` for agent-facing conventions and `docs/` for domain details.
